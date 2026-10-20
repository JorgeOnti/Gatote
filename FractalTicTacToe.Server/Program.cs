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
    /// Gestiona conexiones de jugadores, cuentas, emparejamiento por ELO, validación de movimientos
    /// y flujo de confirmación mutua para deshacer jugadas (Takeback).
    /// </summary>
    public static class Program
    {
        private const int DefaultPort = 7777;
        private const string DataDirectory = "ServerData";

        private static readonly MatchmakingService Matchmaking = new MatchmakingService();
        private static readonly ConcurrentDictionary<string, ClientConnection> ConnectedClientsByPlayerId =
            new ConcurrentDictionary<string, ClientConnection>(StringComparer.Ordinal);

        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : DefaultPort;

            Matchmaking.LoadFromDirectory(DataDirectory);

            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║      FRACTAL TIC-TAC-TOE  •  SERVIDOR MULTIJUGADOR AUTORITATIVO     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"  Puerto TCP       : {port}");
            Console.WriteLine($"  Datos persistidos: ./{DataDirectory}/ (accounts.tsv, matches.pgn)");
            Console.WriteLine("  Protocolo        : LOGIN, QUEUE, MOVE, TAKEBACK_REQ, TAKEBACK_RESP, RESIGN");
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
                            break; // Cliente desconectado
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
                case MultiplayerProtocol.CmdLogin:
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        sender.Send(MultiplayerProtocol.BuildError("Debes proporcionar un nombre de usuario."));
                        return;
                    }

                    PlayerProfile profile = Matchmaking.GetOrCreateAccount(parts[1]);
                    sender.PlayerId = profile.PlayerId;
                    sender.Username = profile.Username;
                    ConnectedClientsByPlayerId[profile.PlayerId] = sender;

                    Matchmaking.SaveToDirectory(DataDirectory);
                    sender.Send(MultiplayerProtocol.BuildAuthOk(profile));
                    Console.WriteLine($"[LOGIN] {profile}");
                    break;

                case MultiplayerProtocol.CmdQueue:
                    if (!EnsureAuthenticated(sender)) return;

                    int maxGap = parts.Length >= 2 && int.TryParse(parts[1], out int g) ? g : 400;
                    OnlineMatchRoom matchedRoom = Matchmaking.EnqueueOrMatchPlayer(sender.PlayerId, maxGap);

                    if (matchedRoom == null)
                    {
                        PlayerProfile p = Matchmaking.GetAccountById(sender.PlayerId);
                        sender.Send($"{MultiplayerProtocol.EvtQueued}|{p.EloRating}");
                        Console.WriteLine($"[COLA] {sender.Username} ({p.EloRating} ELO) buscando oponente...");
                    }
                    else
                    {
                        BindRoomEventsAndNotifyStart(matchedRoom);
                    }
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

                case MultiplayerProtocol.CmdLeaderboard:
                    List<PlayerProfile> top = Matchmaking.GetLeaderboard(10);
                    var sbLb = new StringBuilder("LEADERBOARD");
                    foreach (PlayerProfile item in top)
                    {
                        sbLb.Append('|').Append($"{item.Username}:{item.EloRating}({item.RankTitle})");
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

                default:
                    sender.Send(MultiplayerProtocol.BuildError($"Comando desconocido: {cmd}"));
                    break;
            }
        }

        private static void BindRoomEventsAndNotifyStart(OnlineMatchRoom room)
        {
            Console.WriteLine($"[PARTIDA INICIADA] Sala {room.RoomId}: [X] {room.PlayerX.Username} vs [O] {room.PlayerO.Username}");

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
                Console.WriteLine(
                    $"[FIN DE PARTIDA] Sala {r.RoomId} | Ganador: {record.Winner} ({record.EndReason}) | " +
                    $"ELO X: {record.PlayerXEloBefore}->{record.PlayerXEloAfter}, ELO O: {record.PlayerOEloBefore}->{record.PlayerOEloAfter}");
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

            Matchmaking.RemoveFromQueue(connection.PlayerId);
            ConnectedClientsByPlayerId.TryRemove(connection.PlayerId, out _);

            if (!string.IsNullOrEmpty(connection.ActiveRoomId))
            {
                OnlineMatchRoom activeRoom = Matchmaking.GetRoom(connection.ActiveRoomId);
                activeRoom?.ForfeitByDisconnectOrTimeout(connection.PlayerId, isTimeout: false);
            }
        }

        private static bool EnsureAuthenticated(ClientConnection sender)
        {
            if (string.IsNullOrEmpty(sender.PlayerId))
            {
                sender.Send(MultiplayerProtocol.BuildError("Debes iniciar sesión primero con LOGIN|<username>."));
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
