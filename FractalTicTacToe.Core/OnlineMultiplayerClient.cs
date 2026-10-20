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
    /// Mantiene sincronizado el <see cref="GameState"/> local, el perfil ELO de ambos jugadores,
    /// el emparejamiento (Matchmaking) y las solicitudes de Takeback con consentimiento del oponente.
    /// Compatible con Godot 4 (Android/iOS/Desktop) y aplicaciones de consola.
    /// </summary>
    public sealed class OnlineMultiplayerClient : IDisposable
    {
        private TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _receiveCts;
        private readonly object _sendLock = new object();

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
        public bool IsInQueue { get; private set; }
        public bool IsInActiveMatch => !string.IsNullOrEmpty(ActiveRoomId) && !State.IsGameOver;

        public string LocalPlayerId { get; private set; }
        public string LocalUsername { get; private set; }
        public int LocalElo { get; private set; } = EloRatingCalculator.DefaultInitialRating;
        public string LocalRankTitle { get; private set; } = "Plata";

        public string ActiveRoomId { get; private set; }
        public Player AssignedRole { get; private set; } = Player.None;
        public string OpponentUsername { get; private set; }
        public int OpponentElo { get; private set; } = EloRatingCalculator.DefaultInitialRating;

        public GameState State { get; private set; } = new GameState();
        public Move? LastNetworkMove { get; private set; }

        /// <summary>
        /// Indica si el oponente ha solicitado deshacer su jugada y está esperando nuestra respuesta (Aceptar/Rechazar).
        /// </summary>
        public bool HasIncomingTakebackOffer { get; private set; }
        public Player IncomingTakebackRequester { get; private set; }
        public int IncomingTakebackPlies { get; private set; }

        /// <summary>
        /// Indica si nosotros enviamos una solicitud de Takeback y estamos esperando la decisión del oponente.
        /// </summary>
        public bool IsWaitingForOpponentTakebackResponse { get; private set; }

        // Eventos para actualizar la UI de Godot 4 (usando CallDeferred en el receptor)
        public event Action StateChanged;
        public event Action<string, int, string> Authenticated;
        public event Action<int> QueuedForMatch;
        public event Action<string, Player, string, int, string, int> MatchStarted;
        public event Action<Player, int> TakebackOfferedByOpponent;
        public event Action<bool> TakebackResolved;
        public event Action<Player, MatchEndReason, EloMatchUpdate> MatchEnded;
        public event Action<string[]> LeaderboardReceived;
        public event Action<string[]> HistoryReceived;
        public event Action<string> ErrorReceived;
        public event Action Disconnected;

        /// <summary>
        /// Conecta al servidor TCP en la dirección y puerto indicados e inicia sesión con <paramref name="username"/>.
        /// </summary>
        public async Task ConnectAndLoginAsync(string host, int port, string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("El nombre de usuario no puede estar vacío.", nameof(username));
            }

            Disconnect();

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port).ConfigureAwait(false);

            NetworkStream stream = _tcpClient.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            _receiveCts = new CancellationTokenSource();

            _ = ReceiveLoopAsync(_receiveCts.Token);

            SendRawLine(MultiplayerProtocol.BuildLogin(username.Trim()));
        }

        /// <summary>
        /// Entra en la cola de Matchmaking por ELO para buscar un oponente en línea.
        /// </summary>
        public void JoinMatchmakingQueue(int maxEloGap = 400)
        {
            if (!IsConnected) return;
            SendRawLine(MultiplayerProtocol.BuildQueue(maxEloGap));
        }

        /// <summary>
        /// Envía un movimiento al servidor autoritativo si es nuestro turno en la partida en línea.
        /// </summary>
        public bool TrySendMove(int boardIndex, int cellIndex)
        {
            if (!IsConnected || string.IsNullOrEmpty(ActiveRoomId) || State.IsGameOver)
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

        public void Disconnect()
        {
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
            IsInQueue = false;
            ActiveRoomId = null;
            HasIncomingTakebackOffer = false;
            IsWaitingForOpponentTakebackResponse = false;
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
                // Conexión cerrada
            }
            finally
            {
                IsInQueue = false;
                Disconnected?.Invoke();
                StateChanged?.Invoke();
            }
        }

        /// <summary>
        /// Procesa un paquete proveniente del servidor y actualiza el estado local.
        /// Expuesto como público para pruebas deterministas y transportes personalizados (WebSockets/WebRTC).
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
                    // AUTH_OK|<playerId>|<username>|<elo>|<rank>
                    if (parts.Length >= 5)
                    {
                        LocalPlayerId = parts[1];
                        LocalUsername = parts[2];
                        if (int.TryParse(parts[3], out int elo)) LocalElo = elo;
                        LocalRankTitle = parts[4];
                        Authenticated?.Invoke(LocalUsername, LocalElo, LocalRankTitle);
                        StateChanged?.Invoke();
                    }
                    break;

                case MultiplayerProtocol.EvtQueued:
                    // QUEUED|<elo>
                    IsInQueue = true;
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int qElo)) LocalElo = qElo;
                    QueuedForMatch?.Invoke(LocalElo);
                    StateChanged?.Invoke();
                    break;

                case MultiplayerProtocol.EvtMatchStart:
                    // MATCH_START|<roomId>|<assignedRole>|<userX>|<eloX>|<userO>|<eloO>|<fen>
                    if (parts.Length >= 8)
                    {
                        IsInQueue = false;
                        HasIncomingTakebackOffer = false;
                        IsWaitingForOpponentTakebackResponse = false;
                        LastNetworkMove = null;

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
                    // MATCH_END|<roomId>|<winner>|<reason>|<newEloX>|<deltaX>|<newEloO>|<deltaO>|<fen>
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

                        State = GameSerializer.FromFen(parts[8]);

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

                        LocalRankTitle = EloRatingCalculator.GetRankTitle(LocalElo);
                        var eloUpdate = new EloMatchUpdate(newEloX, deltaX, newEloO, deltaO);
                        MatchEnded?.Invoke(winner, reason, eloUpdate);
                        StateChanged?.Invoke();
                    }
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
            lock (_sendLock)
            {
                try
                {
                    _writer?.WriteLine(line);
                }
                catch
                {
                    // Ignorar si el socket se cerró
                }
            }
        }
    }
}
