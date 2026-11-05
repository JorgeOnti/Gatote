using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Servicio central de cuentas de usuario (Registro y Login con contraseña),
    /// emparejamiento (Matchmaking) filtrado estrictamente por Modo de Tiempo y ventana de puntos,
    /// gestión de salas activas, revanchas e historial persistente de partidas.
    /// </summary>
    public sealed class MatchmakingService
    {
        private struct QueueEntry
        {
            public string PlayerId;
            public TimeControlConfig TimeControl;
        }

        private struct PrivateLobbyEntry
        {
            public string InviteCode;
            public string HostPlayerId;
            public TimeControlConfig TimeControl;
        }

        private static readonly char[] InviteCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

        private readonly object _syncLock = new object();
        private readonly Random _rng = new Random();
        private readonly Dictionary<string, PlayerProfile> _accountsById = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerProfile> _accountsByUsername = new Dictionary<string, PlayerProfile>(StringComparer.OrdinalIgnoreCase);
        private readonly List<QueueEntry> _matchmakingQueue = new List<QueueEntry>();
        private readonly Dictionary<string, PrivateLobbyEntry> _privateLobbiesByCode = new Dictionary<string, PrivateLobbyEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OnlineMatchRoom> _activeRooms = new Dictionary<string, OnlineMatchRoom>(StringComparer.Ordinal);
        private readonly Dictionary<string, OnlineMatchRoom> _finishedRooms = new Dictionary<string, OnlineMatchRoom>(StringComparer.Ordinal);
        private readonly List<MatchRecord> _completedMatches = new List<MatchRecord>();

        /// <summary>
        /// Valida que un nombre de usuario sea seguro (3 a 20 caracteres, sin caracteres de control ni inyección de protocolo).
        /// </summary>
        public static bool IsValidUsername(string username, out string cleanUsername, out string validationError)
        {
            cleanUsername = string.Empty;
            validationError = null;

            if (string.IsNullOrWhiteSpace(username))
            {
                validationError = "El nombre de usuario debe tener entre 3 y 20 caracteres.";
                return false;
            }

            string trimmed = username.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 20)
            {
                validationError = "El nombre de usuario debe tener entre 3 y 20 caracteres.";
                return false;
            }

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                bool allowed = char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' || c == '.';
                if (!allowed || char.IsControl(c) || c == '|' || c == '<' || c == '>' || c == '$' || c == ';' || c == '"' || c == '\'')
                {
                    validationError = "El nombre de usuario solo puede contener letras, números, espacios, guiones o puntos.";
                    return false;
                }
            }

            cleanUsername = trimmed;
            return true;
        }

        /// <summary>
        /// Registra una nueva cuenta de usuario con contraseña. Falla si el nombre de usuario ya está ocupado.
        /// </summary>
        public bool RegisterAccount(string username, string password, out PlayerProfile profile, out string errorMessage)
        {
            profile = null;
            errorMessage = null;

            if (!IsValidUsername(username, out string cleanName, out string userErr))
            {
                errorMessage = userErr;
                return false;
            }

            if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 4 || password.Trim().Length > 64)
            {
                errorMessage = "La contraseña debe tener entre 4 y 64 caracteres.";
                return false;
            }

            lock (_syncLock)
            {
                if (_accountsByUsername.ContainsKey(cleanName))
                {
                    errorMessage = $"El usuario '{cleanName}' ya está registrado. Inicia sesión con tu contraseña.";
                    return false;
                }

                string newId = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                profile = new PlayerProfile(newId, cleanName, EloRatingCalculator.DefaultInitialRating);
                profile.SetPassword(password.Trim());

                _accountsById[profile.PlayerId] = profile;
                _accountsByUsername[cleanName] = profile;
                return true;
            }
        }

        /// <summary>
        /// Autentica una cuenta existente verificando su usuario y contraseña.
        /// </summary>
        public bool AuthenticateAccount(string username, string password, out PlayerProfile profile, out string errorMessage)
        {
            profile = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(username))
            {
                errorMessage = "Debes ingresar tu nombre de usuario.";
                return false;
            }

            string cleanName = username.Trim();

            lock (_syncLock)
            {
                if (!_accountsByUsername.TryGetValue(cleanName, out PlayerProfile existing))
                {
                    errorMessage = $"No existe ninguna cuenta con el usuario '{cleanName}'. Regístrate primero.";
                    return false;
                }

                if (!string.IsNullOrEmpty(existing.PasswordHash) && !existing.VerifyPassword(password ?? string.Empty))
                {
                    errorMessage = "Contraseña incorrecta.";
                    return false;
                }

                // Si era una cuenta antigua sin contraseña y el usuario proporcionó una al iniciar sesión, se guarda
                if (string.IsNullOrEmpty(existing.PasswordHash) && !string.IsNullOrWhiteSpace(password))
                {
                    existing.SetPassword(password.Trim());
                }

                profile = existing;
                return true;
            }
        }

        /// <summary>
        /// Registra una nueva cuenta o recupera la existente por nombre de usuario (uso directo/pruebas).
        /// </summary>
        public PlayerProfile GetOrCreateAccount(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("El nombre de usuario no puede estar vacío.", nameof(username));
            }

            string cleanName = username.Trim();

            lock (_syncLock)
            {
                if (_accountsByUsername.TryGetValue(cleanName, out PlayerProfile existing))
                {
                    return existing;
                }

                string newId = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                var profile = new PlayerProfile(newId, cleanName, EloRatingCalculator.DefaultInitialRating);
                _accountsById[profile.PlayerId] = profile;
                _accountsByUsername[cleanName] = profile;
                return profile;
            }
        }

        public PlayerProfile GetAccountById(string playerId)
        {
            lock (_syncLock)
            {
                return _accountsById.TryGetValue(playerId, out PlayerProfile p) ? p : null;
            }
        }

        /// <summary>
        /// Elimina permanentemente la cuenta y datos asociados del jugador (Requisito de Google Play y Apple App Store).
        /// </summary>
        public bool DeleteAccount(string username, string password, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(username))
            {
                errorMessage = "Nombre de usuario no especificado.";
                return false;
            }

            string cleanName = username.Trim();
            lock (_syncLock)
            {
                if (!_accountsByUsername.TryGetValue(cleanName, out PlayerProfile existing))
                {
                    errorMessage = "La cuenta no existe.";
                    return false;
                }

                if (!string.IsNullOrEmpty(existing.PasswordHash) &&
                    !string.IsNullOrEmpty(password) &&
                    !existing.VerifyPassword(password))
                {
                    errorMessage = "Contraseña incorrecta para eliminar la cuenta.";
                    return false;
                }

                RemoveFromQueueInternal(existing.PlayerId);
                _accountsByUsername.Remove(cleanName);
                _accountsById.Remove(existing.PlayerId);
                return true;
            }
        }

        /// <summary>
        /// Sobrecarga por defecto que encola en <see cref="TimeControlConfig.Blitz3Plus2"/>.
        /// </summary>
        public OnlineMatchRoom EnqueueOrMatchPlayer(string playerId, int maxEloGap = 400)
        {
            return EnqueueOrMatchPlayer(playerId, TimeControlConfig.Blitz3Plus2, maxEloGap);
        }

        /// <summary>
        /// Coloca a un jugador en la cola de Matchmaking para un <paramref name="timeControl"/> específico
        /// y busca únicamente entre los jugadores que seleccionaron ESE MISMO modo de tiempo y estén dentro de <paramref name="maxEloGap"/>.
        /// </summary>
        public OnlineMatchRoom EnqueueOrMatchPlayer(string playerId, TimeControlConfig timeControl, int maxEloGap = 400)
        {
            lock (_syncLock)
            {
                if (!_accountsById.TryGetValue(playerId, out PlayerProfile seeker))
                {
                    throw new InvalidOperationException("La cuenta de jugador no existe.");
                }

                // Actualizar o agregar la entrada del jugador en la cola con el modo de tiempo elegido
                RemoveFromQueueInternal(playerId);
                _matchmakingQueue.Add(new QueueEntry
                {
                    PlayerId = playerId,
                    TimeControl = timeControl
                });

                string targetTimeCode = timeControl.ToCode();
                int bestCandidateIndex = -1;
                int smallestDiff = int.MaxValue;

                for (int i = 0; i < _matchmakingQueue.Count; i++)
                {
                    QueueEntry candidateEntry = _matchmakingQueue[i];
                    if (string.Equals(candidateEntry.PlayerId, playerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // REGLA ESTRICTA: Solo emparejar jugadores que hayan seleccionado exactamente el mismo modo de tiempo
                    if (!string.Equals(candidateEntry.TimeControl.ToCode(), targetTimeCode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    PlayerProfile candidate = _accountsById[candidateEntry.PlayerId];
                    int seekerModeRating = seeker.GetRatingForTimeControl(timeControl);
                    int candidateModeRating = candidate.GetRatingForTimeControl(timeControl);
                    int diff = Math.Abs(seekerModeRating - candidateModeRating);

                    if (diff <= maxEloGap && diff < smallestDiff)
                    {
                        smallestDiff = diff;
                        bestCandidateIndex = i;
                    }
                }

                if (bestCandidateIndex < 0)
                {
                    return null; // Queda esperando un rival del mismo modo de tiempo
                }

                string opponentId = _matchmakingQueue[bestCandidateIndex].PlayerId;
                RemoveFromQueueInternal(opponentId);
                RemoveFromQueueInternal(playerId);

                PlayerProfile opponent = _accountsById[opponentId];

                bool opponentGetsX = ((opponent.GamesPlayed + seeker.GamesPlayed) & 1) == 0;
                PlayerProfile playerX = opponentGetsX ? opponent : seeker;
                PlayerProfile playerO = opponentGetsX ? seeker : opponent;

                string roomId = "room_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                var room = new OnlineMatchRoom(roomId, playerX, playerO, timeControl);

                room.MatchConcluded += OnRoomMatchConcluded;
                _activeRooms[roomId] = room;

                return room;
            }
        }

        /// <summary>
        /// Crea una sala privada con un código de invitación corto de 4 caracteres para jugar con un amigo.
        /// </summary>
        public string CreatePrivateLobby(string hostPlayerId, TimeControlConfig timeControl)
        {
            lock (_syncLock)
            {
                if (!_accountsById.ContainsKey(hostPlayerId))
                {
                    throw new InvalidOperationException("La cuenta de jugador no existe.");
                }

                RemoveFromQueueInternal(hostPlayerId);

                string code;
                do
                {
                    char[] chars = new char[4];
                    for (int i = 0; i < 4; i++)
                    {
                        chars[i] = InviteCodeAlphabet[_rng.Next(InviteCodeAlphabet.Length)];
                    }
                    code = new string(chars);
                } while (_privateLobbiesByCode.ContainsKey(code));

                _privateLobbiesByCode[code] = new PrivateLobbyEntry
                {
                    InviteCode = code,
                    HostPlayerId = hostPlayerId,
                    TimeControl = timeControl
                };

                return code;
            }
        }

        /// <summary>
        /// Une a un jugador a una sala privada mediante su código de 4 caracteres e inicia la partida.
        /// </summary>
        public OnlineMatchRoom JoinPrivateLobby(string guestPlayerId, string inviteCode, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(inviteCode))
            {
                errorMessage = "Ingresa un código de sala válido.";
                return null;
            }

            string cleanCode = inviteCode.Trim().ToUpperInvariant();

            lock (_syncLock)
            {
                if (!_accountsById.TryGetValue(guestPlayerId, out PlayerProfile guest))
                {
                    errorMessage = "Debes iniciar sesión primero.";
                    return null;
                }

                if (!_privateLobbiesByCode.TryGetValue(cleanCode, out PrivateLobbyEntry lobby))
                {
                    errorMessage = $"No existe ninguna sala activa con el código '{cleanCode}'.";
                    return null;
                }

                if (string.Equals(lobby.HostPlayerId, guestPlayerId, StringComparison.Ordinal))
                {
                    errorMessage = "No puedes unirte a tu propia sala privada.";
                    return null;
                }

                if (!_accountsById.TryGetValue(lobby.HostPlayerId, out PlayerProfile host))
                {
                    _privateLobbiesByCode.Remove(cleanCode);
                    errorMessage = "El anfitrión de la sala ya no está disponible.";
                    return null;
                }

                _privateLobbiesByCode.Remove(cleanCode);
                RemoveFromQueueInternal(host.PlayerId);
                RemoveFromQueueInternal(guest.PlayerId);

                bool hostGetsX = ((host.GamesPlayed + guest.GamesPlayed) & 1) == 0;
                PlayerProfile playerX = hostGetsX ? host : guest;
                PlayerProfile playerO = hostGetsX ? guest : host;

                string roomId = "room_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                var room = new OnlineMatchRoom(roomId, playerX, playerO, lobby.TimeControl);
                room.MatchConcluded += OnRoomMatchConcluded;
                _activeRooms[roomId] = room;

                return room;
            }
        }

        /// <summary>
        /// Crea una sala de revancha directa invirtiendo los roles (X <-> O) y conservando el control de tiempo.
        /// </summary>
        public OnlineMatchRoom CreateRematchRoom(OnlineMatchRoom previousRoom)
        {
            if (previousRoom == null) throw new ArgumentNullException(nameof(previousRoom));

            lock (_syncLock)
            {
                _finishedRooms.Remove(previousRoom.RoomId);
                string newRoomId = "room_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                var rematchRoom = new OnlineMatchRoom(
                    newRoomId,
                    previousRoom.PlayerO,
                    previousRoom.PlayerX,
                    previousRoom.TimeControl);

                rematchRoom.MatchConcluded += OnRoomMatchConcluded;
                _activeRooms[newRoomId] = rematchRoom;
                return rematchRoom;
            }
        }

        public bool RemoveFromQueue(string playerId)
        {
            lock (_syncLock)
            {
                return RemoveFromQueueInternal(playerId);
            }
        }

        private bool RemoveFromQueueInternal(string playerId)
        {
            bool removed = false;
            for (int i = _matchmakingQueue.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_matchmakingQueue[i].PlayerId, playerId, StringComparison.Ordinal))
                {
                    _matchmakingQueue.RemoveAt(i);
                    removed = true;
                }
            }

            List<string> codesToRemove = null;
            foreach (var kvp in _privateLobbiesByCode)
            {
                if (string.Equals(kvp.Value.HostPlayerId, playerId, StringComparison.Ordinal))
                {
                    if (codesToRemove == null) codesToRemove = new List<string>();
                    codesToRemove.Add(kvp.Key);
                }
            }

            if (codesToRemove != null)
            {
                foreach (string c in codesToRemove)
                {
                    _privateLobbiesByCode.Remove(c);
                    removed = true;
                }
            }

            return removed;
        }

        public OnlineMatchRoom GetRoom(string roomId)
        {
            lock (_syncLock)
            {
                if (_activeRooms.TryGetValue(roomId, out OnlineMatchRoom room))
                {
                    return room;
                }
                return _finishedRooms.TryGetValue(roomId, out OnlineMatchRoom fin) ? fin : null;
            }
        }

        /// <summary>
        /// Busca si el jugador tiene actualmente una partida en línea en curso sin concluir.
        /// Permite restaurar la partida en el recuadro de Partidas Activas (estilo Lichess).
        /// </summary>
        public bool TryFindActiveRoomForPlayer(string playerId, out OnlineMatchRoom activeRoom, out Player role)
        {
            activeRoom = null;
            role = Player.None;
            List<OnlineMatchRoom> rooms = GetActiveRoomsForPlayer(playerId);
            if (rooms.Count > 0)
            {
                activeRoom = rooms[rooms.Count - 1];
                role = activeRoom.GetPlayerRole(playerId);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Devuelve todas las salas en línea activas (no concluidas) en las que participa actualmente <paramref name="playerId"/>.
        /// </summary>
        public List<OnlineMatchRoom> GetActiveRoomsForPlayer(string playerId)
        {
            var result = new List<OnlineMatchRoom>();
            if (string.IsNullOrEmpty(playerId))
            {
                return result;
            }

            lock (_syncLock)
            {
                foreach (OnlineMatchRoom room in _activeRooms.Values)
                {
                    if (room.IsMatchOver) continue;
                    if (room.GetPlayerRole(playerId) != Player.None)
                    {
                        result.Add(room);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Obtiene la tabla de clasificación (Leaderboard) ordenada de mayor a menor puntaje.
        /// </summary>
        public List<PlayerProfile> GetLeaderboard(int maxCount = 20)
        {
            return GetLeaderboard(null, maxCount);
        }

        /// <summary>
        /// Obtiene la tabla de clasificación ordenada por un modo de juego específico (<paramref name="modeFilter"/>) o general.
        /// </summary>
        public List<PlayerProfile> GetLeaderboard(TimeControlConfig? modeFilter, int maxCount = 20)
        {
            lock (_syncLock)
            {
                var list = new List<PlayerProfile>(_accountsById.Values);
                list.Sort((a, b) =>
                {
                    int eloA = modeFilter.HasValue ? a.GetRatingForTimeControl(modeFilter.Value) : a.EloRating;
                    int eloB = modeFilter.HasValue ? b.GetRatingForTimeControl(modeFilter.Value) : b.EloRating;
                    int cmp = eloB.CompareTo(eloA);
                    return cmp != 0 ? cmp : b.Wins.CompareTo(a.Wins);
                });

                if (list.Count > maxCount)
                {
                    list.RemoveRange(maxCount, list.Count - maxCount);
                }

                return list;
            }
        }

        /// <summary>
        /// Retorna el historial de partidas concluidas en las que participó un jugador dado.
        /// </summary>
        public List<MatchRecord> GetPlayerMatchHistory(string username, int maxCount = 20)
        {
            lock (_syncLock)
            {
                var result = new List<MatchRecord>();
                for (int i = _completedMatches.Count - 1; i >= 0 && result.Count < maxCount; i--)
                {
                    MatchRecord match = _completedMatches[i];
                    if (string.Equals(match.PlayerXName, username, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(match.PlayerOName, username, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(match);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Persiste en disco las cuentas de usuario (incluyendo hash de contraseña y puntajes por modo) y el historial de partidas concluidas.
        /// </summary>
        public void SaveToDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("La ruta de directorio no puede estar vacía.", nameof(directoryPath));
            }

            lock (_syncLock)
            {
                Directory.CreateDirectory(directoryPath);

                string accountsFile = Path.Combine(directoryPath, "accounts.tsv");
                var sbAccounts = new StringBuilder();
                foreach (PlayerProfile p in _accountsById.Values)
                {
                    sbAccounts.AppendLine(
                        $"{p.PlayerId}\t{p.Username}\t{p.EloRating}\t{p.Wins}\t{p.Losses}\t{p.Draws}\t{p.PasswordHash}\t{p.BulletElo}\t{p.Blitz3Plus2Elo}\t{p.RapidElo}\t{p.Blitz3Plus0Elo}");
                }
                File.WriteAllText(accountsFile, sbAccounts.ToString(), Encoding.UTF8);

                string matchesFile = Path.Combine(directoryPath, "matches.pgn");
                var sbMatches = new StringBuilder();
                foreach (MatchRecord m in _completedMatches)
                {
                    sbMatches.AppendLine(m.SerializeToText().Trim());
                    sbMatches.AppendLine("---");
                }
                File.WriteAllText(matchesFile, sbMatches.ToString(), Encoding.UTF8);
            }
        }

        /// <summary>
        /// Carga desde disco las cuentas y el historial de partidas si existen.
        /// </summary>
        public void LoadFromDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            lock (_syncLock)
            {
                string accountsFile = Path.Combine(directoryPath, "accounts.tsv");
                if (File.Exists(accountsFile))
                {
                    _accountsById.Clear();
                    _accountsByUsername.Clear();

                    string[] lines = File.ReadAllLines(accountsFile, Encoding.UTF8);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string[] cols = line.Split('\t');
                        if (cols.Length < 6) continue;

                        int baseElo = int.TryParse(cols[2], out int elo) ? elo : EloRatingCalculator.DefaultInitialRating;
                        var profile = new PlayerProfile(cols[0], cols[1], baseElo)
                        {
                            Wins = int.TryParse(cols[3], out int w) ? w : 0,
                            Losses = int.TryParse(cols[4], out int l) ? l : 0,
                            Draws = int.TryParse(cols[5], out int d) ? d : 0,
                            PasswordHash = cols.Length >= 7 ? cols[6] : string.Empty,
                            BulletElo = cols.Length >= 8 && int.TryParse(cols[7], out int bElo) ? bElo : baseElo,
                            Blitz3Plus2Elo = cols.Length >= 9 && int.TryParse(cols[8], out int zElo) ? zElo : baseElo,
                            RapidElo = cols.Length >= 10 && int.TryParse(cols[9], out int rElo) ? rElo : baseElo,
                            Blitz3Plus0Elo = cols.Length >= 11 && int.TryParse(cols[10], out int z0Elo) ? z0Elo : baseElo
                        };

                        _accountsById[profile.PlayerId] = profile;
                        _accountsByUsername[profile.Username] = profile;
                    }
                }

                string matchesFile = Path.Combine(directoryPath, "matches.pgn");
                if (File.Exists(matchesFile))
                {
                    _completedMatches.Clear();
                    string content = File.ReadAllText(matchesFile, Encoding.UTF8);
                    string[] blocks = content.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string block in blocks)
                    {
                        if (string.IsNullOrWhiteSpace(block)) continue;
                        _completedMatches.Add(MatchRecord.DeserializeFromText(block));
                    }
                }
            }
        }

        private void OnRoomMatchConcluded(OnlineMatchRoom room, MatchRecord record, EloMatchUpdate eloUpdate)
        {
            lock (_syncLock)
            {
                if (record.EndReason != MatchEndReason.Aborted)
                {
                    _completedMatches.Add(record);
                }
                _activeRooms.Remove(room.RoomId);
                _finishedRooms[room.RoomId] = room;
            }
        }
    }
}
