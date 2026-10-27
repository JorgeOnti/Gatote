using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FractalTicTacToe.Core;

namespace FractalTicTacToe.Server
{
    /// <summary>
    /// Servidor dedicado autoritativo en tiempo real para Fractal Tic-Tac-Toe Online.
    /// Gestiona registro/inicio de sesión con contraseña, emparejamiento estricto por control de tiempo y puntaje,
    /// validación de movimientos, Takeback con consentimiento mutuo, derrota por tiempo, cancelación a los 30s y revanchas.
    /// </summary>
    public static class Program
    {
        private const int DefaultPort = 7777;
        private const int DisconnectGracePeriodMs = 20_000;
        private static string DataDirectory = "ServerData";

        private static readonly MatchmakingService Matchmaking = new MatchmakingService();
        private static readonly ConcurrentDictionary<string, ClientConnection> ConnectedClientsByPlayerId =
            new ConcurrentDictionary<string, ClientConnection>(StringComparer.Ordinal);

        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int port = DefaultPort;
            if (args.Length > 0 && int.TryParse(args[0], out int cliPort))
            {
                port = cliPort;
            }
            else if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out int envPort))
            {
                port = envPort;
            }

            string envDataDir = Environment.GetEnvironmentVariable("SERVER_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(envDataDir))
            {
                DataDirectory = envDataDir.Trim();
            }

            Matchmaking.LoadFromDirectory(DataDirectory);
            GameLogger.ConfigureFileSink(Path.Combine(DataDirectory, "server.log"));
            GameLogger.Info("SERVER", $"Servidor TCP iniciado en puerto {port} (Datos: {DataDirectory}).");

            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║      FRACTAL TIC-TAC-TOE  •  SERVIDOR MULTIJUGADOR AUTORITATIVO     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"  Puerto TCP       : {port}");
            Console.WriteLine($"  Localhost PC     : 127.0.0.1:{port}");
            Console.WriteLine($"  Emulador Android : 10.0.2.2:{port}");
            foreach (string lanIp in GetLocalIPv4Addresses())
            {
                Console.WriteLine($"  Red Wi-Fi / LAN  : {lanIp}:{port}  (Usa esta IP en tu celular Android físico)");
            }
            Console.WriteLine($"  Datos persistidos: ./{DataDirectory}/ (accounts.tsv, matches.pgn)");
            Console.WriteLine("  Protocolo        : REGISTER, LOGIN, RESUME, QUEUE, PRIVATE, MOVE, TAKEBACK, REMATCH");
            Console.WriteLine("  Presiona Ctrl+C para detener el servidor de forma segura.\n");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    TcpClient tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = HandleClientAsync(tcpClient, cts.Token);
                }
            }
            catch (ObjectDisposedException)
            {
                // Cierre normal del listener
            }
            finally
            {
                listener.Stop();
                Matchmaking.SaveToDirectory(DataDirectory);
                Console.WriteLine("Servidor detenido y datos guardados correctamente.");
            }
        }

        private static async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
        {
            var connection = new ClientConnection(tcpClient);
            Console.WriteLine($"[CONEXIÓN] Cliente conectado desde {tcpClient.Client.RemoteEndPoint}");

            try
            {
                using (tcpClient)
                using (var stream = tcpClient.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    connection.Writer = writer;

                    while (!ct.IsCancellationRequested)
                    {
                        string rawLine = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (rawLine == null)
                        {
                            break;
                        }

                        ProcessCommand(connection, rawLine);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DESCONEXIÓN] {connection.Username ?? "Anónimo"}: {ex.Message}");
            }
            finally
            {
                HandleClientDisconnect(connection);
            }
        }

        private static void ProcessCommand(ClientConnection sender, string rawLine)
        {
            string[] parts = MultiplayerProtocol.SplitPacket(rawLine);
            if (parts.Length == 0)
            {
                return;
            }

            string cmd = parts[0].ToUpperInvariant();

            switch (cmd)
            {
                case MultiplayerProtocol.CmdRegister:
                    if (parts.Length < 3)
                    {
                        sender.Send(MultiplayerProtocol.BuildError("Debes proporcionar usuario y contraseña para registrarte."));
                        return;
                    }

                    if (!Matchmaking.RegisterAccount(parts[1], parts[2], out PlayerProfile registeredProfile, out string regError))
                    {
                        sender.Send(MultiplayerProtocol.BuildError(regError));
                        return;
                    }

                    CompleteClientAuthentication(sender, registeredProfile, "REGISTRO");
                    break;

                case MultiplayerProtocol.CmdLogin:
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        sender.Send(MultiplayerProtocol.BuildError("Debes proporcionar un nombre de usuario."));
                        return;
                    }

                    PlayerProfile loginProfile;
                    if (parts.Length >= 3)
                    {
                        if (!Matchmaking.AuthenticateAccount(parts[1], parts[2], out loginProfile, out string authError))
                        {
                            sender.Send(MultiplayerProtocol.BuildError(authError));
                            return;
                        }
                    }
                    else
                    {
                        loginProfile = Matchmaking.GetOrCreateAccount(parts[1]);
                    }

                    CompleteClientAuthentication(sender, loginProfile, "LOGIN");
                    break;

                case MultiplayerProtocol.CmdResume:
                    if (parts.Length < 4 || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[3]))
                    {
                        sender.Send(MultiplayerProtocol.BuildError("Formato RESUME inválido."));
                        return;
                    }

                    PlayerProfile resumeProfile;
                    if (!string.IsNullOrEmpty(parts[2]))
                    {
                        if (!Matchmaking.AuthenticateAccount(parts[1], parts[2], out resumeProfile, out string resErr))
                        {
                            sender.Send(MultiplayerProtocol.BuildError(resErr));
                            return;
                        }
                    }
                    else
                    {
                        resumeProfile = Matchmaking.GetOrCreateAccount(parts[1]);
                    }

                    CompleteClientAuthentication(sender, resumeProfile, "RECONEXIÓN");
                    OnlineMatchRoom resumeRoom = Matchmaking.GetRoom(parts[3]);
                    if (resumeRoom != null && !resumeRoom.IsMatchOver)
                    {
                        Player resumedRole = resumeRoom.GetPlayerRole(resumeProfile.PlayerId);
                        if (resumedRole != Player.None)
                        {
                            sender.ActiveRoomId = resumeRoom.RoomId;
                            sender.Send(MultiplayerProtocol.BuildSessionResumed(resumeRoom, resumedRole));
                            Console.WriteLine($"[SESIÓN REANUDADA] {sender.Username} retomó su asiento [{resumedRole}] en la sala {resumeRoom.RoomId}.");
                        }
                    }
                    break;

                case MultiplayerProtocol.CmdQueue:
                    if (!EnsureAuthenticated(sender)) return;

                    int maxGap = parts.Length >= 2 && int.TryParse(parts[1], out int g) ? g : 400;
                    TimeControlConfig requestedTime = parts.Length >= 3
                        ? TimeControlConfig.FromCode(parts[2])
                        : TimeControlConfig.Blitz3Plus2;

                    OnlineMatchRoom matchedRoom = Matchmaking.EnqueueOrMatchPlayer(sender.PlayerId, requestedTime, maxGap);

                    if (matchedRoom == null)
                    {
                        PlayerProfile p = Matchmaking.GetAccountById(sender.PlayerId);
                        sender.Send($"{MultiplayerProtocol.EvtQueued}|{p.EloRating}|{requestedTime.ToCode()}");
                        Console.WriteLine($"[COLA] {sender.Username} ({p.EloRating} pts) buscando rival en modo {requestedTime.Name} ({requestedTime.ToCode()})...");
                    }
                    else
                    {
                        BindRoomEventsAndNotifyStart(matchedRoom);
                    }
                    break;

                case MultiplayerProtocol.CmdCancelQueue:
                    if (!EnsureAuthenticated(sender)) return;
                    Matchmaking.RemoveFromQueue(sender.PlayerId);
                    sender.Send(MultiplayerProtocol.EvtQueueCancelled);
                    Console.WriteLine($"[COLA CANCELADA] {sender.Username} salió de la cola de búsqueda.");
                    break;

                case MultiplayerProtocol.CmdCreatePrivate:
                    if (!EnsureAuthenticated(sender)) return;
                    TimeControlConfig privateTc = parts.Length >= 2
                        ? TimeControlConfig.FromCode(parts[1])
                        : TimeControlConfig.Blitz3Plus2;
                    string inviteCode = Matchmaking.CreatePrivateLobby(sender.PlayerId, privateTc);
                    sender.Send(MultiplayerProtocol.BuildPrivateCreated(inviteCode, privateTc.ToCode()));
                    Console.WriteLine($"[SALA PRIVADA] {sender.Username} creó la sala '{inviteCode}' ({privateTc.Name}).");
                    break;

                case MultiplayerProtocol.CmdJoinPrivate:
                    if (!EnsureAuthenticated(sender)) return;
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        sender.Send(MultiplayerProtocol.BuildError("Ingresa el código de la sala privada."));
                        return;
                    }
                    OnlineMatchRoom privateRoom = Matchmaking.JoinPrivateLobby(sender.PlayerId, parts[1], out string joinErr);
                    if (privateRoom == null)
                    {
                        sender.Send(MultiplayerProtocol.BuildError(joinErr));
                        return;
                    }
                    BindRoomEventsAndNotifyStart(privateRoom);
                    break;

                case MultiplayerProtocol.CmdMove:
                    if (!EnsureAuthenticated(sender)) return;
                    if (parts.Length < 4 ||
                        !int.TryParse(parts[2], out int bIdx) ||
                        !int.TryParse(parts[3], out int cIdx))
                    {
                        sender.Send(MultiplayerProtocol.BuildError("Formato de movimiento inválido."));
                        return;
                    }

                    OnlineMatchRoom moveRoom = Matchmaking.GetRoom(parts[1]);
                    if (moveRoom == null)
                    {
                        sender.Send(MultiplayerProtocol.BuildError("La sala indicada no existe o ya concluyó."));
                        return;
                    }

                    if (!moveRoom.SubmitMove(sender.PlayerId, new Move(bIdx, cIdx), out string moveErr))
                    {
                        sender.Send(MultiplayerProtocol.BuildError(moveErr));
                    }
                    break;

                case MultiplayerProtocol.CmdTakebackRequest:
                    if (!EnsureAuthenticated(sender)) return;
                    if (parts.Length < 2)
                    {
                        sender.Send(MultiplayerProtocol.BuildError("Debes indicar el ID de la sala."));
                        return;
                    }

                    OnlineMatchRoom tbReqRoom = Matchmaking.GetRoom(parts[1]);
                    if (tbReqRoom == null)
                    {
                        sender.Send(MultiplayerProtocol.BuildError("La sala indicada no existe."));
                        return;
                    }

                    if (!tbReqRoom.RequestTakeback(sender.PlayerId, out _, out string tbReqErr))
                    {
                        sender.Send(MultiplayerProtocol.BuildError(tbReqErr));
                    }
                    break;

                case MultiplayerProtocol.CmdTakebackRespond:
                    if (!EnsureAuthenticated(sender)) return;
                    if (parts.Length < 3)
                    {
                        sender.Send(MultiplayerProtocol.BuildError("Formato TAKEBACK_RESP inválido."));
                        return;
                    }

                    OnlineMatchRoom tbRespRoom = Matchmaking.GetRoom(parts[1]);
                    if (tbRespRoom == null)
                    {
                        sender.Send(MultiplayerProtocol.BuildError("La sala indicada no existe."));
                        return;
                    }

                    bool accept = string.Equals(parts[2], "ACCEPT", StringComparison.OrdinalIgnoreCase);
                    if (!tbRespRoom.RespondToTakeback(sender.PlayerId, accept, out string tbRespErr))
                    {
                        sender.Send(MultiplayerProtocol.BuildError(tbRespErr));
                    }
                    break;

                case MultiplayerProtocol.CmdResign:
                    if (!EnsureAuthenticated(sender)) return;
                    if (parts.Length < 2) return;

                    OnlineMatchRoom resignRoom = Matchmaking.GetRoom(parts[1]);
                    if (resignRoom != null && !resignRoom.Resign(sender.PlayerId, out string resignErr))
                    {
                        sender.Send(MultiplayerProtocol.BuildError(resignErr));
                    }
                    break;

                case MultiplayerProtocol.CmdTimeout:
                    if (!EnsureAuthenticated(sender)) return;
                    if (parts.Length < 2) return;

                    OnlineMatchRoom timeoutRoom = Matchmaking.GetRoom(parts[1]);
                    timeoutRoom?.ForfeitByDisconnectOrTimeout(sender.PlayerId, isTimeout: true);
                    break;

                case MultiplayerProtocol.CmdAbort:
                    if (!EnsureAuthenticated(sender)) return;
                    if (parts.Length < 2) return;

                    OnlineMatchRoom abortRoom = Matchmaking.GetRoom(parts[1]);
                    abortRoom?.AbortByFirstMoveTimeout();
                    break;

                case MultiplayerProtocol.CmdRematchRequest:
                    if (!EnsureAuthenticated(sender)) return;
                    if (parts.Length < 2) return;

                    OnlineMatchRoom prevRoom = Matchmaking.GetRoom(parts[1]);
                    if (prevRoom != null && prevRoom.RegisterRematchVote(sender.PlayerId, out Player reqRole, out bool bothAccepted))
                    {
                        if (bothAccepted)
                        {
                            OnlineMatchRoom rematchRoom = Matchmaking.CreateRematchRoom(prevRoom);
                            BindRoomEventsAndNotifyStart(rematchRoom);
                        }
                        else
                        {
                            BroadcastToRoom(prevRoom, MultiplayerProtocol.BuildRematchOffered(prevRoom, reqRole));
                            Console.WriteLine($"[REVANCHA SOLICITADA] Sala {prevRoom.RoomId}: Jugador {reqRole} invita a una revancha.");
                        }
                    }
                    break;

                case MultiplayerProtocol.CmdRematchRespond:
                    if (!EnsureAuthenticated(sender)) return;
                    if (parts.Length < 3) return;

                    OnlineMatchRoom remRespRoom = Matchmaking.GetRoom(parts[1]);
                    if (remRespRoom == null) return;

                    bool acceptRematch = string.Equals(parts[2], "ACCEPT", StringComparison.OrdinalIgnoreCase);
                    if (acceptRematch)
                    {
                        if (remRespRoom.RegisterRematchVote(sender.PlayerId, out _, out bool bothNowAccepted) && bothNowAccepted)
                        {
                            OnlineMatchRoom rematchRoom = Matchmaking.CreateRematchRoom(remRespRoom);
                            BindRoomEventsAndNotifyStart(rematchRoom);
                        }
                    }
                    else
                    {
                        Player declinerRole = remRespRoom.GetPlayerRole(sender.PlayerId);
                        remRespRoom.ClearRematchVotes();
                        BroadcastToRoom(remRespRoom, MultiplayerProtocol.BuildRematchDeclined(remRespRoom, declinerRole));
                        Console.WriteLine($"[REVANCHA RECHAZADA] Sala {remRespRoom.RoomId}: Jugador {declinerRole} rechazó la revancha.");
                    }
                    break;

                case MultiplayerProtocol.CmdLeaderboard:
                    List<PlayerProfile> top = Matchmaking.GetLeaderboard(10);
                    var sbLb = new StringBuilder("LEADERBOARD");
                    for (int i = 0; i < top.Count; i++)
                    {
                        PlayerProfile item = top[i];
                        sbLb.Append('|').Append($"{i + 1}:{item.Username}:{item.EloRating}:{item.RankTitle}:{item.Wins}V-{item.Losses}D-{item.Draws}E");
                    }
                    sender.Send(sbLb.ToString());
                    break;

                case MultiplayerProtocol.CmdHistory:
                    string targetUser = parts.Length >= 2 ? parts[1] : sender.Username;
                    List<MatchRecord> history = Matchmaking.GetPlayerMatchHistory(targetUser, 10);
                    var sbHist = new StringBuilder($"HISTORY|{targetUser}|{history.Count}");
                    foreach (MatchRecord rec in history)
                    {
                        sbHist.Append('|').Append($"{rec.MatchId}:{rec.PlayerXName}({rec.PlayerXEloAfter})vs{rec.PlayerOName}({rec.PlayerOEloAfter}):W={rec.Winner}");
                    }
                    sender.Send(sbHist.ToString());
                    break;

                case MultiplayerProtocol.CmdDeleteAccount:
                    if (!EnsureAuthenticated(sender)) return;
                    string delPass = parts.Length >= 2 ? parts[1] : string.Empty;
                    string deletedUsername = sender.Username;
                    if (!Matchmaking.DeleteAccount(deletedUsername, delPass, out string delErr))
                    {
                        sender.Send(MultiplayerProtocol.BuildError(delErr));
                        return;
                    }

                    ConnectedClientsByPlayerId.TryRemove(sender.PlayerId, out _);
                    sender.PlayerId = null;
                    sender.Username = null;
                    Matchmaking.SaveToDirectory(DataDirectory);
                    sender.Send(MultiplayerProtocol.BuildAccountDeleted(deletedUsername));
                    Console.WriteLine($"[CUENTA ELIMINADA] Usuario '{deletedUsername}' eliminó permanentemente su cuenta.");
                    break;

                default:
                    sender.Send(MultiplayerProtocol.BuildError($"Comando desconocido: {cmd}"));
                    break;
            }
        }

        private static void CompleteClientAuthentication(ClientConnection sender, PlayerProfile profile, string tag)
        {
            sender.PlayerId = profile.PlayerId;
            sender.Username = profile.Username;
            ConnectedClientsByPlayerId[profile.PlayerId] = sender;

            Matchmaking.SaveToDirectory(DataDirectory);
            sender.Send(MultiplayerProtocol.BuildAuthOk(profile));
            Console.WriteLine($"[{tag}] {profile}");
        }

        private static void BindRoomEventsAndNotifyStart(OnlineMatchRoom room)
        {
            string startMsg = $"Sala {room.RoomId} ({room.TimeControl.Name}): [X] {room.PlayerX.Username} vs [O] {room.PlayerO.Username}";
            Console.WriteLine($"[PARTIDA INICIADA] {startMsg}");
            GameLogger.Info("SERVER", $"Partida iniciada — {startMsg}");

            if (ConnectedClientsByPlayerId.TryGetValue(room.PlayerX.PlayerId, out ClientConnection clientX))
            {
                clientX.ActiveRoomId = room.RoomId;
                clientX.Send(MultiplayerProtocol.BuildMatchStart(room, Player.X));
            }

            if (ConnectedClientsByPlayerId.TryGetValue(room.PlayerO.PlayerId, out ClientConnection clientO))
            {
                clientO.ActiveRoomId = room.RoomId;
                clientO.Send(MultiplayerProtocol.BuildMatchStart(room, Player.O));
            }

            // Vigilante autoritativo de 30 segundos en el servidor para el primer movimiento
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(MatchClock.FirstMoveAbortLimitSeconds + 0.5)).ConfigureAwait(false);
                if (!room.IsMatchOver && room.State.MoveCount == 0)
                {
                    room.AbortByFirstMoveTimeout();
                }
            });

            room.MoveAccepted += (r, move, role) =>
            {
                string packet = MultiplayerProtocol.BuildStateSync(r, move);
                BroadcastToRoom(r, packet);
            };

            room.TakebackRequested += (r, req) =>
            {
                string packet = MultiplayerProtocol.BuildTakebackOffered(r, req);
                BroadcastToRoom(r, packet);
                Console.WriteLine($"[TAKEBACK SOLICITADO] Sala {r.RoomId}: Jugador {req.RequesterRole} pide deshacer {req.PliesToUndo} medio(s) turno(s).");
            };

            room.TakebackResolved += (r, req, accepted) =>
            {
                string packet = MultiplayerProtocol.BuildTakebackResolved(r, accepted);
                BroadcastToRoom(r, packet);
                Console.WriteLine($"[TAKEBACK RESUELTO] Sala {r.RoomId}: {(accepted ? "ACEPTADO" : "RECHAZADO")}.");
            };

            room.MatchConcluded += (r, record, eloUpdate) =>
            {
                string packet = MultiplayerProtocol.BuildMatchEnd(r, record, eloUpdate);
                BroadcastToRoom(r, packet);
                Matchmaking.SaveToDirectory(DataDirectory);
                string endMsg =
                    $"Sala {r.RoomId} | Ganador: {record.Winner} ({record.EndReason}) | " +
                    $"Pts X: {record.PlayerXEloBefore}->{record.PlayerXEloAfter}, Pts O: {record.PlayerOEloBefore}->{record.PlayerOEloAfter}";
                Console.WriteLine($"[FIN DE PARTIDA] {endMsg}");
                GameLogger.Info("SERVER", $"Fin de partida — {endMsg}");
            };
        }

        private static void BroadcastToRoom(OnlineMatchRoom room, string packet)
        {
            if (ConnectedClientsByPlayerId.TryGetValue(room.PlayerX.PlayerId, out ClientConnection cx))
            {
                cx.Send(packet);
            }

            if (ConnectedClientsByPlayerId.TryGetValue(room.PlayerO.PlayerId, out ClientConnection co))
            {
                co.Send(packet);
            }
        }

        private static void HandleClientDisconnect(ClientConnection connection)
        {
            if (string.IsNullOrEmpty(connection.PlayerId))
            {
                return;
            }

            string disconnectedPlayerId = connection.PlayerId;
            string disconnectedRoomId = connection.ActiveRoomId;

            Matchmaking.RemoveFromQueue(disconnectedPlayerId);

            // Solo remover del diccionario si la conexión actual sigue siendo exactamente esta instancia
            if (ConnectedClientsByPlayerId.TryGetValue(disconnectedPlayerId, out ClientConnection activeConn) &&
                ReferenceEquals(activeConn, connection))
            {
                ConnectedClientsByPlayerId.TryRemove(disconnectedPlayerId, out _);
            }

            if (!string.IsNullOrEmpty(disconnectedRoomId))
            {
                OnlineMatchRoom activeRoom = Matchmaking.GetRoom(disconnectedRoomId);
                if (activeRoom != null && !activeRoom.IsMatchOver)
                {
                    if (activeRoom.State.MoveCount == 0)
                    {
                        // Si aún no se hacía el primer movimiento, abortar sin puntos tras una breve ventana
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(3000).ConfigureAwait(false);
                            if (!ConnectedClientsByPlayerId.ContainsKey(disconnectedPlayerId) && !activeRoom.IsMatchOver)
                            {
                                activeRoom.AbortByFirstMoveTimeout();
                            }
                        });
                    }
                    else
                    {
                        // Ventana de gracia de 20 segundos para reconexión ante micro-cortes de red móvil
                        Console.WriteLine($"[VENTANA DE RECONEXIÓN] Esperando 20s a {connection.Username} en sala {disconnectedRoomId}...");
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(DisconnectGracePeriodMs).ConfigureAwait(false);
                            if (!ConnectedClientsByPlayerId.ContainsKey(disconnectedPlayerId) && !activeRoom.IsMatchOver)
                            {
                                activeRoom.ForfeitByDisconnectOrTimeout(disconnectedPlayerId, isTimeout: false);
                            }
                        });
                    }
                }
            }
        }

        private static List<string> GetLocalIPv4Addresses()
        {
            var result = new List<string>();
            try
            {
                string hostName = Dns.GetHostName();
                IPAddress[] addresses = Dns.GetHostAddresses(hostName);
                foreach (IPAddress ip in addresses)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        result.Add(ip.ToString());
                    }
                }
            }
            catch
            {
                // Ignorar si no se pueden enumerar interfaces
            }
            return result;
        }

        private static bool EnsureAuthenticated(ClientConnection sender)
        {
            if (string.IsNullOrEmpty(sender.PlayerId))
            {
                sender.Send(MultiplayerProtocol.BuildError("Debes iniciar sesión primero."));
                return false;
            }
            return true;
        }

        private sealed class ClientConnection
        {
            private readonly object _writeLock = new object();
            public TcpClient TcpClient { get; }
            public StreamWriter Writer { get; set; }
            public string PlayerId { get; set; }
            public string Username { get; set; }
            public string ActiveRoomId { get; set; }

            public ClientConnection(TcpClient tcpClient)
            {
                TcpClient = tcpClient;
            }

            public void Send(string packetLine)
            {
                lock (_writeLock)
                {
                    try
                    {
                        Writer?.WriteLine(packetLine);
                    }
                    catch
                    {
                        // Ignorar errores de socket cerrado
                    }
                }
            }
        }
    }
}
