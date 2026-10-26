using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Cliente de red asíncrono para conectarse al servidor autoritativo <c>FractalTicTacToe.Server</c>.
    /// Gestiona autenticación (Login y Registro), cola de Matchmaking filtrada por control de tiempo,
    /// temporizador de 30s para el primer movimiento, relojes Fischer, Takeback mutuo y revanchas.
    /// </summary>
    public sealed class OnlineMultiplayerClient : IDisposable
    {
        private TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _receiveCts;
        private readonly object _sendLock = new object();
        private volatile bool _intentionalDisconnect;
        private volatile bool _handlingUnexpectedDisconnect;

        public const int DefaultConnectTimeoutMs = 3000;

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
        public bool IsAuthenticated => !string.IsNullOrEmpty(LocalPlayerId);
        public bool IsInQueue { get; private set; }
        public string PendingInviteCode { get; private set; }
        public bool IsInActiveMatch => !string.IsNullOrEmpty(ActiveRoomId) && !State.IsGameOver;

        public string LocalPlayerId { get; private set; }
        public string LocalUsername { get; private set; }
        public int LocalElo { get; private set; } = EloRatingCalculator.DefaultInitialRating;
        public string LocalRankTitle { get; private set; } = "Plata";
        public int LocalWins { get; private set; }
        public int LocalLosses { get; private set; }
        public int LocalDraws { get; private set; }

        public string ActiveRoomId { get; private set; }
        public Player AssignedRole { get; private set; } = Player.None;
        public string OpponentUsername { get; private set; }
        public int OpponentElo { get; private set; } = EloRatingCalculator.DefaultInitialRating;

        public GameState State { get; private set; } = new GameState();
        public MatchClock Clock { get; } = new MatchClock(TimeControlConfig.Blitz3Plus2);
        public Move? LastNetworkMove { get; private set; }
        public EloMatchUpdate? LastRatingUpdate { get; private set; }
        public MatchEndReason LastEndReason { get; private set; } = MatchEndReason.InProgress;
        public MatchRecord LastCompletedMatchRecord { get; private set; }

        public bool HasIncomingTakebackOffer { get; private set; }
        public Player IncomingTakebackRequester { get; private set; }
        public int IncomingTakebackPlies { get; private set; }
        public bool IsWaitingForOpponentTakebackResponse { get; private set; }

        public bool HasRequestedRematch { get; private set; }
        public bool HasOpponentOfferedRematch { get; private set; }
        public bool RematchDeclinedByOpponent { get; private set; }

        private bool _pendingQueueAfterAuth;
        private TimeControlConfig _pendingQueueTimeControl = TimeControlConfig.Blitz3Plus2;
        private int _pendingQueueMaxEloGap = 400;

        // Eventos para actualizar la UI de Godot 4 y .NET MAUI
        public event Action StateChanged;
        public event Action<string, int, string> Authenticated;
        public event Action<int> QueuedForMatch;
        public event Action<string, string> PrivateLobbyCreated;
        public event Action<string, Player, string, int, string, int> MatchStarted;
        public event Action<Player, int> TakebackOfferedByOpponent;
        public event Action<bool> TakebackResolved;
        public event Action<Player, MatchEndReason, EloMatchUpdate> MatchEnded;
        public event Action<string[]> LeaderboardReceived;
        public event Action<string[]> HistoryReceived;
        public event Action<string> AccountDeleted;
        public event Action<string> ErrorReceived;
        public event Action<string> ServerConnectionLost;
        public event Action Disconnected;

        /// <summary>
        /// Conecta al servidor TCP e inicia sesión con <paramref name="username"/> y <paramref name="password"/>.
        /// </summary>
        public async Task ConnectAndLoginAsync(string host, int port, string username, string password = "")
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("El nombre de usuario no puede estar vacío.", nameof(username));
            }

            await EnsureSocketConnectedAsync(host, port).ConfigureAwait(false);
            SendRawLine(MultiplayerProtocol.BuildLogin(username.Trim(), password ?? string.Empty));
        }

        /// <summary>
        /// Conecta al servidor TCP y registra una nueva cuenta con <paramref name="username"/> y <paramref name="password"/>.
        /// </summary>
        public async Task ConnectAndRegisterAsync(string host, int port, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("El nombre de usuario no puede estar vacío.", nameof(username));
            }

            await EnsureSocketConnectedAsync(host, port).ConfigureAwait(false);
            SendRawLine(MultiplayerProtocol.BuildRegister(username.Trim(), password ?? string.Empty));
        }

        private async Task EnsureSocketConnectedAsync(string host, int port)
        {
            if (IsConnected)
            {
                return;
            }

            Disconnect();
            _intentionalDisconnect = false;
            _handlingUnexpectedDisconnect = false;

            var tcp = new TcpClient();
            try
            {
                Task connectTask = tcp.ConnectAsync(host, port);
                Task completed = await Task.WhenAny(connectTask, Task.Delay(DefaultConnectTimeoutMs)).ConfigureAwait(false);
                if (completed != connectTask || connectTask.IsFaulted || !tcp.Connected)
                {
                    try { tcp.Close(); } catch { }
                    throw new TimeoutException("No se pudo conectar al servidor.");
                }

                await connectTask.ConfigureAwait(false);
            }
            catch
            {
                try { tcp.Close(); } catch { }
                throw;
            }

            _tcpClient = tcp;
            NetworkStream stream = _tcpClient.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            _receiveCts = new CancellationTokenSource();

            _ = ReceiveLoopAsync(_receiveCts.Token);
        }

        /// <summary>
        /// Crea una sala privada en el servidor con el control de tiempo indicado y genera un código de 4 letras/números.
        /// </summary>
        public void CreatePrivateLobby(TimeControlConfig timeControl)
        {
            if (!IsConnected || !IsAuthenticated) return;
            Clock.Reset(timeControl);
            IsInQueue = false;
            PendingInviteCode = "...";
            SendRawLine(MultiplayerProtocol.BuildCreatePrivate(timeControl.ToCode()));
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Se une a una sala privada creada por un amigo usando su código de 4 caracteres.
        /// </summary>
        public void JoinPrivateLobby(string inviteCode)
        {
            if (!IsConnected || !IsAuthenticated || string.IsNullOrWhiteSpace(inviteCode)) return;
            IsInQueue = false;
            PendingInviteCode = null;
            SendRawLine(MultiplayerProtocol.BuildJoinPrivate(inviteCode.Trim().ToUpperInvariant()));
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Entra en la cola de Matchmaking filtrada por el control de tiempo actual (<see cref="Clock.Config"/>).
        /// </summary>
        public void JoinMatchmakingQueue(int maxEloGap = 400)
        {
            JoinMatchmakingQueue(Clock.Config, maxEloGap);
        }

        /// <summary>
        /// Entra en la cola de Matchmaking filtrada estrictamente por <paramref name="timeControl"/> y ventana de puntos.
        /// </summary>
        public void JoinMatchmakingQueue(TimeControlConfig timeControl, int maxEloGap = 400)
        {
            if (!IsConnected) return;
            Clock.Reset(timeControl);
            HasRequestedRematch = false;
            HasOpponentOfferedRematch = false;
            RematchDeclinedByOpponent = false;
            IsInQueue = true;

            if (!IsAuthenticated)
            {
                _pendingQueueAfterAuth = true;
                _pendingQueueTimeControl = timeControl;
                _pendingQueueMaxEloGap = maxEloGap;
                StateChanged?.Invoke();
                return;
            }

            _pendingQueueAfterAuth = false;
            SendRawLine(MultiplayerProtocol.BuildQueue(maxEloGap, timeControl.ToCode()));
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Cancela la búsqueda actual en la cola de Matchmaking o sala privada.
        /// </summary>
        public void CancelMatchmakingQueue()
        {
            _pendingQueueAfterAuth = false;
            IsInQueue = false;
            PendingInviteCode = null;
            if (IsConnected)
            {
                SendRawLine(MultiplayerProtocol.BuildCancelQueue());
            }
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Solicita o acepta una revancha contra el mismo oponente tras finalizar una partida en línea.
        /// </summary>
        public bool RequestRematch()
        {
            if (!IsConnected || string.IsNullOrEmpty(ActiveRoomId) || !State.IsGameOver)
            {
                return false;
            }

            RematchDeclinedByOpponent = false;
            HasRequestedRematch = true;
            SendRawLine(MultiplayerProtocol.BuildRematchRequest(ActiveRoomId));
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Envía nuestra respuesta explícita (Aceptar o Rechazar) a la invitación de revancha del oponente.
        /// </summary>
        public bool RespondToOpponentRematch(bool accept)
        {
            if (!IsConnected || string.IsNullOrEmpty(ActiveRoomId) || !HasOpponentOfferedRematch)
            {
                return false;
            }

            HasOpponentOfferedRematch = false;
            if (accept)
            {
                HasRequestedRematch = true;
            }
            SendRawLine(MultiplayerProtocol.BuildRematchResponse(ActiveRoomId, accept));
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Envía un movimiento al servidor autoritativo si es nuestro turno en la partida en línea.
        /// </summary>
        public bool TrySendMove(int boardIndex, int cellIndex)
        {
            if (!IsConnected || string.IsNullOrEmpty(ActiveRoomId) || State.IsGameOver || Clock.IsAbortedByFirstMoveTimeout)
            {
                return false;
            }

            if (State.CurrentPlayer != AssignedRole)
            {
                return false;
            }

            var move = new Move(boardIndex, cellIndex);
            if (!State.IsValidMove(move))
            {
                return false;
            }

            SendRawLine(MultiplayerProtocol.BuildMove(ActiveRoomId, move));
            return true;
        }

        /// <summary>
        /// Envía al servidor una solicitud para deshacer la última jugada (requiere aceptación del oponente).
        /// </summary>
        public bool RequestTakeback()
        {
            if (!IsConnected || string.IsNullOrEmpty(ActiveRoomId) || State.IsGameOver || !State.CanUndo)
            {
                return false;
            }

            if (HasIncomingTakebackOffer || IsWaitingForOpponentTakebackResponse)
            {
                return false;
            }

            IsWaitingForOpponentTakebackResponse = true;
            SendRawLine(MultiplayerProtocol.BuildTakebackRequest(ActiveRoomId));
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Envía nuestra respuesta (Aceptar o Rechazar) a la solicitud de Takeback del oponente.
        /// </summary>
        public bool RespondToOpponentTakeback(bool accept)
        {
            if (!IsConnected || string.IsNullOrEmpty(ActiveRoomId) || !HasIncomingTakebackOffer)
            {
                return false;
            }

            HasIncomingTakebackOffer = false;
            SendRawLine(MultiplayerProtocol.BuildTakebackResponse(ActiveRoomId, accept));
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Envía la rendición en la partida actual.
        /// </summary>
        public void ResignMatch()
        {
            if (!IsConnected || string.IsNullOrEmpty(ActiveRoomId) || State.IsGameOver)
            {
                return;
            }

            SendRawLine(MultiplayerProtocol.BuildResign(ActiveRoomId));
        }

        /// <summary>
        /// Actualiza el temporizador de 30s de primer movimiento o el reloj principal en curso.
        /// Notifica automáticamente al servidor si expira los 30s sin primer tiro (ABORT) o si el reloj llega a 0 (TIMEOUT).
        /// </summary>
        public bool TickClock(double deltaSeconds)
        {
            if (!IsInActiveMatch)
            {
                return false;
            }

            bool expired = Clock.Tick(deltaSeconds);
            if (expired)
            {
                if (Clock.IsAbortedByFirstMoveTimeout)
                {
                    // Bloquear inmediatamente el tablero local al vencer los 30s sin primer movimiento
                    State.ForceConclude(Player.None);
                    LastEndReason = MatchEndReason.Aborted;
                    Clock.Pause();
                    SendRawLine(MultiplayerProtocol.BuildAbort(ActiveRoomId));
                    StateChanged?.Invoke();
                }
                else if (Clock.FlaggedPlayer == AssignedRole)
                {
                    SendRawLine(MultiplayerProtocol.BuildTimeout(ActiveRoomId));
                }
            }

            return expired;
        }

        public void RequestLeaderboard()
        {
            if (!IsConnected) return;
            SendRawLine(MultiplayerProtocol.CmdLeaderboard);
        }

        public void RequestMatchHistory(string username = null)
        {
            if (!IsConnected) return;
            string target = string.IsNullOrWhiteSpace(username) ? LocalUsername : username.Trim();
            SendRawLine($"{MultiplayerProtocol.CmdHistory}|{target}");
        }

        public void RequestDeleteAccount(string password = "")
        {
            if (!IsConnected) return;
            SendRawLine(MultiplayerProtocol.BuildDeleteAccount(password));
        }

        public void Disconnect()
        {
            _intentionalDisconnect = true;

            try
            {
                _receiveCts?.Cancel();
            }
            catch
            {
                // Ignorar
            }

            try
            {
                _tcpClient?.Close();
            }
            catch
            {
                // Ignorar
            }

            _tcpClient = null;
            _reader = null;
            _writer = null;
            LocalPlayerId = null;
            IsInQueue = false;
            PendingInviteCode = null;
            ActiveRoomId = null;
            HasIncomingTakebackOffer = false;
            IsWaitingForOpponentTakebackResponse = false;
            HasRequestedRematch = false;
            HasOpponentOfferedRematch = false;
            Clock.Pause();
        }

        /// <summary>
        /// Simula o procesa la caída inesperada del servidor: cancela inmediatamente cualquier partida en curso
        /// o búsqueda activa sin sumar ni restar puntos a ningún jugador y notifica a la UI para volver a Inicio.
        /// </summary>
        public void HandleUnexpectedServerDisconnect(string customMessage = null)
        {
            if (_intentionalDisconnect || _handlingUnexpectedDisconnect)
            {
                return;
            }

            _handlingUnexpectedDisconnect = true;

            try
            {
                _receiveCts?.Cancel();
            }
            catch
            {
                // Ignorar
            }

            try
            {
                _tcpClient?.Close();
            }
            catch
            {
                // Ignorar
            }

            _tcpClient = null;
            _reader = null;
            _writer = null;
            LocalPlayerId = null;

            bool hadActiveMatch = !string.IsNullOrEmpty(ActiveRoomId) && !State.IsGameOver;
            IsInQueue = false;
            PendingInviteCode = null;
            ActiveRoomId = null;
            HasIncomingTakebackOffer = false;
            IsWaitingForOpponentTakebackResponse = false;
            HasRequestedRematch = false;
            HasOpponentOfferedRematch = false;

            Clock.Pause();

            if (hadActiveMatch)
            {
                State.ForceConclude(Player.None);
            }

            // Nadie gana ni pierde puntos cuando cae el servidor
            LastRatingUpdate = null;
            LastEndReason = MatchEndReason.Aborted;

            string msg = string.IsNullOrWhiteSpace(customMessage)
                ? "Se perdió la conexión con el servidor. La partida fue cancelada sin cambio de puntos."
                : customMessage;

            ServerConnectionLost?.Invoke(msg);
            Disconnected?.Invoke();
            StateChanged?.Invoke();
        }

        public void Dispose()
        {
            Disconnect();
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _reader != null)
                {
                    string rawLine = await _reader.ReadLineAsync().ConfigureAwait(false);
                    if (rawLine == null)
                    {
                        break;
                    }

                    ProcessServerPacket(rawLine);
                }
            }
            catch
            {
                // Conexión cerrada abruptamente
            }
            finally
            {
                if (!_intentionalDisconnect)
                {
                    HandleUnexpectedServerDisconnect();
                }
                else
                {
                    IsInQueue = false;
                    PendingInviteCode = null;
                    Disconnected?.Invoke();
                    StateChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// Procesa un paquete proveniente del servidor y actualiza el estado local.
        /// </summary>
        public void ProcessServerPacket(string rawLine)
        {
            string[] parts = MultiplayerProtocol.SplitPacket(rawLine);
            if (parts.Length == 0)
            {
                return;
            }

            string evt = parts[0].ToUpperInvariant();

            switch (evt)
            {
                case MultiplayerProtocol.EvtAuthOk:
                    // AUTH_OK|<playerId>|<username>|<elo>|<rank>|<wins>|<losses>|<draws>
                    if (parts.Length >= 5)
                    {
                        LocalPlayerId = parts[1];
                        LocalUsername = parts[2];
                        if (int.TryParse(parts[3], out int elo)) LocalElo = elo;
                        LocalRankTitle = parts[4];
                        if (parts.Length >= 8)
                        {
                            if (int.TryParse(parts[5], out int w)) LocalWins = w;
                            if (int.TryParse(parts[6], out int l)) LocalLosses = l;
                            if (int.TryParse(parts[7], out int d)) LocalDraws = d;
                        }
                        Authenticated?.Invoke(LocalUsername, LocalElo, LocalRankTitle);

                        if (_pendingQueueAfterAuth)
                        {
                            _pendingQueueAfterAuth = false;
                            SendRawLine(MultiplayerProtocol.BuildQueue(_pendingQueueMaxEloGap, _pendingQueueTimeControl.ToCode()));
                        }

                        StateChanged?.Invoke();
                    }
                    break;

                case MultiplayerProtocol.EvtQueued:
                    // QUEUED|<elo>|<timeControlCode>
                    IsInQueue = true;
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int qElo)) LocalElo = qElo;
                    QueuedForMatch?.Invoke(LocalElo);
                    StateChanged?.Invoke();
                    break;

                case MultiplayerProtocol.EvtQueueCancelled:
                    IsInQueue = false;
                    PendingInviteCode = null;
                    StateChanged?.Invoke();
                    break;

                case MultiplayerProtocol.EvtPrivateCreated:
                    // PRIVATE_CREATED|<inviteCode>|<timeControlCode>
                    if (parts.Length >= 2)
                    {
                        IsInQueue = false;
                        PendingInviteCode = parts[1];
                        string tcCode = parts.Length >= 3 ? parts[2] : Clock.Config.ToCode();
                        PrivateLobbyCreated?.Invoke(PendingInviteCode, tcCode);
                        StateChanged?.Invoke();
                    }
                    break;

                case MultiplayerProtocol.EvtMatchStart:
                    // MATCH_START|<roomId>|<assignedRole>|<userX>|<eloX>|<userO>|<eloO>|<fen>|<timeControlCode>
                    if (parts.Length >= 8)
                    {
                        IsInQueue = false;
                        PendingInviteCode = null;
                        HasIncomingTakebackOffer = false;
                        IsWaitingForOpponentTakebackResponse = false;
                        HasRequestedRematch = false;
                        HasOpponentOfferedRematch = false;
                        RematchDeclinedByOpponent = false;
                        LastNetworkMove = null;
                        LastRatingUpdate = null;
                        LastEndReason = MatchEndReason.InProgress;

                        TimeControlConfig roomTimeControl = parts.Length >= 9
                            ? TimeControlConfig.FromCode(parts[8])
                            : Clock.Config;

                        Clock.Reset(roomTimeControl);
                        // REGLA ESTILO LICHESS: El reloj principal NO corre hasta el primer movimiento;
                        // hay 30 segundos para realizar el primer tiro o la partida se cancela.
                        Clock.StartFirstMoveCountdown(MatchClock.FirstMoveAbortLimitSeconds);

                        ActiveRoomId = parts[1];
                        Enum.TryParse(parts[2], ignoreCase: true, out Player role);
                        AssignedRole = role;

                        string userX = parts[3];
                        int.TryParse(parts[4], out int eloX);
                        string userO = parts[5];
                        int.TryParse(parts[6], out int eloO);

                        if (AssignedRole == Player.X)
                        {
                            LocalUsername = userX;
                            LocalElo = eloX;
                            OpponentUsername = userO;
                            OpponentElo = eloO;
                        }
                        else
                        {
                            LocalUsername = userO;
                            LocalElo = eloO;
                            OpponentUsername = userX;
                            OpponentElo = eloX;
                        }

                        State = GameSerializer.FromFen(parts[7]);
                        MatchStarted?.Invoke(ActiveRoomId, AssignedRole, userX, eloX, userO, eloO);
                        StateChanged?.Invoke();
                    }
                    break;

                case MultiplayerProtocol.EvtStateSync:
                    // STATE_SYNC|<roomId>|<lastBoard>|<lastCell>|<fen>|<movesTranscript>
                    if (parts.Length >= 5)
                    {
                        HasIncomingTakebackOffer = false;
                        IsWaitingForOpponentTakebackResponse = false;

                        if (int.TryParse(parts[2], out int b) && int.TryParse(parts[3], out int c))
                        {
                            LastNetworkMove = new Move(b, c);
                        }

                        if (parts.Length >= 6 && !string.IsNullOrWhiteSpace(parts[5]))
                        {
                            State = GameSerializer.ImportMoveHistory(parts[5]);
                        }
                        else
                        {
                            State = GameSerializer.FromFen(parts[4]);
                        }

                        Player mover = State.CurrentPlayer == Player.X ? Player.O : Player.X;
                        Clock.OnMoveCompleted(mover, State.CurrentPlayer);
                        StateChanged?.Invoke();
                    }
                    break;

                case MultiplayerProtocol.EvtTakebackOffered:
                    // TAKEBACK_OFFERED|<roomId>|<requesterRole>|<pliesToUndo>
                    if (parts.Length >= 4)
                    {
                        Enum.TryParse(parts[2], ignoreCase: true, out Player reqRole);
                        int.TryParse(parts[3], out int plies);

                        if (reqRole != AssignedRole)
                        {
                            HasIncomingTakebackOffer = true;
                            IncomingTakebackRequester = reqRole;
                            IncomingTakebackPlies = plies;
                            TakebackOfferedByOpponent?.Invoke(reqRole, plies);
                        }
                        else
                        {
                            IsWaitingForOpponentTakebackResponse = true;
                        }

                        StateChanged?.Invoke();
                    }
                    break;

                case MultiplayerProtocol.EvtTakebackResolved:
                    // TAKEBACK_RESOLVED|<roomId>|<ACCEPTED|DECLINED>|<fen>|<movesTranscript>
                    if (parts.Length >= 4)
                    {
                        bool accepted = string.Equals(parts[2], "ACCEPTED", StringComparison.OrdinalIgnoreCase);
                        HasIncomingTakebackOffer = false;
                        IsWaitingForOpponentTakebackResponse = false;

                        if (accepted)
                        {
                            if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
                            {
                                State = GameSerializer.ImportMoveHistory(parts[4]);
                            }
                            else
                            {
                                State = GameSerializer.FromFen(parts[3]);
                            }
                            LastNetworkMove = State.GetLastMove();
                        }

                        TakebackResolved?.Invoke(accepted);
                        StateChanged?.Invoke();
                    }
                    break;

                case MultiplayerProtocol.EvtMatchEnd:
                    // MATCH_END|<roomId>|<winner>|<reason>|<newEloX>|<deltaX>|<newEloO>|<deltaO>|<fen>|<movesTranscript>
                    if (parts.Length >= 9)
                    {
                        HasIncomingTakebackOffer = false;
                        IsWaitingForOpponentTakebackResponse = false;

                        Enum.TryParse(parts[2], ignoreCase: true, out Player winner);
                        Enum.TryParse(parts[3], ignoreCase: true, out MatchEndReason reason);
                        int.TryParse(parts[4], out int newEloX);
                        int.TryParse(parts[5], out int deltaX);
                        int.TryParse(parts[6], out int newEloO);
                        int.TryParse(parts[7], out int deltaO);

                        string transcript = parts.Length >= 10 ? parts[9] : string.Empty;
                        if (!string.IsNullOrWhiteSpace(transcript))
                        {
                            State = GameSerializer.ImportMoveHistory(transcript);
                        }
                        else
                        {
                            State = GameSerializer.FromFen(parts[8]);
                        }

                        // Garantizar que State quede marcado como terminado con el ganador efectivo (p. ej. en Timeout / Rendición)
                        State.ForceConclude(winner);

                        string userX = AssignedRole == Player.X ? LocalUsername : OpponentUsername;
                        string userO = AssignedRole == Player.O ? LocalUsername : OpponentUsername;

                        if (AssignedRole == Player.X)
                        {
                            LocalElo = newEloX;
                            OpponentElo = newEloO;
                        }
                        else if (AssignedRole == Player.O)
                        {
                            LocalElo = newEloO;
                            OpponentElo = newEloX;
                        }

                        if (reason != MatchEndReason.Aborted)
                        {
                            if (winner == Player.None) LocalDraws++;
                            else if (winner == AssignedRole) LocalWins++;
                            else LocalLosses++;
                        }

                        LocalRankTitle = EloRatingCalculator.GetRankTitle(LocalElo);
                        var eloUpdate = new EloMatchUpdate(newEloX, deltaX, newEloO, deltaO);
                        LastRatingUpdate = eloUpdate;
                        LastEndReason = reason;
                        Clock.Pause();

                        LastCompletedMatchRecord = new MatchRecord
                        {
                            MatchId = parts[1],
                            PlayedAtUtc = DateTime.UtcNow,
                            PlayerXName = userX ?? "Jugador X",
                            PlayerOName = userO ?? "Jugador O",
                            PlayerXEloBefore = newEloX - deltaX,
                            PlayerOEloBefore = newEloO - deltaO,
                            PlayerXEloAfter = newEloX,
                            PlayerOEloAfter = newEloO,
                            Winner = winner,
                            EndReason = reason,
                            FinalFen = GameSerializer.ToFen(State),
                            MoveTranscript = transcript
                        };

                        MatchEnded?.Invoke(winner, reason, eloUpdate);
                        StateChanged?.Invoke();
                    }
                    break;

                case MultiplayerProtocol.EvtRematchOffered:
                    // REMATCH_OFFERED|<roomId>|<requesterRole>
                    if (parts.Length >= 3)
                    {
                        Enum.TryParse(parts[2], ignoreCase: true, out Player remRole);
                        if (remRole != AssignedRole)
                        {
                            HasOpponentOfferedRematch = true;
                            RematchDeclinedByOpponent = false;
                        }
                        else
                        {
                            HasRequestedRematch = true;
                            RematchDeclinedByOpponent = false;
                        }
                        StateChanged?.Invoke();
                    }
                    break;

                case MultiplayerProtocol.EvtRematchDeclined:
                    // REMATCH_DECLINED|<roomId>|<declinerRole>
                    HasRequestedRematch = false;
                    HasOpponentOfferedRematch = false;
                    RematchDeclinedByOpponent = true;
                    StateChanged?.Invoke();
                    break;

                case MultiplayerProtocol.EvtAccountDeleted:
                    // ACCOUNT_DELETED|<username>
                    string deletedUser = parts.Length >= 2 ? parts[1] : LocalUsername;
                    LocalPlayerId = null;
                    LocalUsername = null;
                    IsInQueue = false;
                    ActiveRoomId = null;
                    AccountDeleted?.Invoke(deletedUser ?? string.Empty);
                    StateChanged?.Invoke();
                    break;

                case "LEADERBOARD":
                    if (parts.Length > 1)
                    {
                        var entries = new string[parts.Length - 1];
                        Array.Copy(parts, 1, entries, 0, entries.Length);
                        LeaderboardReceived?.Invoke(entries);
                    }
                    break;

                case "HISTORY":
                    if (parts.Length > 3)
                    {
                        var records = new string[parts.Length - 3];
                        Array.Copy(parts, 3, records, 0, records.Length);
                        HistoryReceived?.Invoke(records);
                    }
                    break;

                case MultiplayerProtocol.EvtError:
                    if (parts.Length >= 2)
                    {
                        IsWaitingForOpponentTakebackResponse = false;
                        ErrorReceived?.Invoke(parts[1]);
                        StateChanged?.Invoke();
                    }
                    break;
            }
        }

        private void SendRawLine(string line)
        {
            bool failed = false;
            lock (_sendLock)
            {
                try
                {
                    if (_writer == null || _tcpClient == null || !_tcpClient.Connected)
                    {
                        failed = true;
                    }
                    else
                    {
                        _writer.WriteLine(line);
                    }
                }
                catch
                {
                    failed = true;
                }
            }

            if (failed && !_intentionalDisconnect)
            {
                HandleUnexpectedServerDisconnect();
            }
        }
    }
}
