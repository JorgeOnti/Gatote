using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Servicio central de cuentas de usuario, emparejamiento (Matchmaking) basado en rating ELO,
    /// gestión de salas activas e historial persistente de partidas.
    /// </summary>
    public sealed class MatchmakingService
    {
        private readonly object _syncLock = new object();
        private readonly Dictionary<string, PlayerProfile> _accountsById = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerProfile> _accountsByUsername = new Dictionary<string, PlayerProfile>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _matchmakingQueue = new List<string>();
        private readonly Dictionary<string, OnlineMatchRoom> _activeRooms = new Dictionary<string, OnlineMatchRoom>(StringComparer.Ordinal);
        private readonly List<MatchRecord> _completedMatches = new List<MatchRecord>();

        /// <summary>
        /// Registra una nueva cuenta o recupera la existente por nombre de usuario.
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
        /// Coloca a un jugador en la cola de Matchmaking y busca inmediatamente al oponente en espera
        /// con el ELO más cercano dentro de <paramref name="maxEloGap"/>.
        /// Si encuentra rival, crea y retorna la sala <see cref="OnlineMatchRoom"/>; de lo contrario retorna null.
        /// </summary>
        public OnlineMatchRoom EnqueueOrMatchPlayer(string playerId, int maxEloGap = 400)
        {
            lock (_syncLock)
            {
                if (!_accountsById.TryGetValue(playerId, out PlayerProfile seeker))
                {
                    throw new InvalidOperationException("La cuenta de jugador no existe.");
                }

                // Evitar duplicados en la cola
                if (!_matchmakingQueue.Contains(playerId))
                {
                    _matchmakingQueue.Add(playerId);
                }

                int bestCandidateIndex = -1;
                int smallestDiff = int.MaxValue;

                for (int i = 0; i < _matchmakingQueue.Count; i++)
                {
                    string candidateId = _matchmakingQueue[i];
                    if (string.Equals(candidateId, playerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    PlayerProfile candidate = _accountsById[candidateId];
                    int diff = Math.Abs(seeker.EloRating - candidate.EloRating);

                    if (diff <= maxEloGap && diff < smallestDiff)
                    {
                        smallestDiff = diff;
                        bestCandidateIndex = i;
                    }
                }

                if (bestCandidateIndex < 0)
                {
                    return null; // Queda en espera de un rival compatible
                }

                string opponentId = _matchmakingQueue[bestCandidateIndex];
                _matchmakingQueue.RemoveAt(bestCandidateIndex);
                _matchmakingQueue.Remove(playerId);

                PlayerProfile opponent = _accountsById[opponentId];

                // Asignar X al jugador que llevaba más tiempo esperando (opponent) y O al recién llegado (seeker),
                // o alternar según el número total de partidas jugadas para balancear el primer turno.
                bool opponentGetsX = ((opponent.GamesPlayed + seeker.GamesPlayed) & 1) == 0;
                PlayerProfile playerX = opponentGetsX ? opponent : seeker;
                PlayerProfile playerO = opponentGetsX ? seeker : opponent;

                string roomId = "room_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                var room = new OnlineMatchRoom(roomId, playerX, playerO);

                room.MatchConcluded += OnRoomMatchConcluded;
                _activeRooms[roomId] = room;

                return room;
            }
        }

        public bool RemoveFromQueue(string playerId)
        {
            lock (_syncLock)
            {
                return _matchmakingQueue.Remove(playerId);
            }
        }

        public OnlineMatchRoom GetRoom(string roomId)
        {
            lock (_syncLock)
            {
                return _activeRooms.TryGetValue(roomId, out OnlineMatchRoom room) ? room : null;
            }
        }

        /// <summary>
        /// Obtiene la tabla de clasificación (Leaderboard) ordenada de mayor a menor rating ELO.
        /// </summary>
        public List<PlayerProfile> GetLeaderboard(int maxCount = 20)
        {
            lock (_syncLock)
            {
                var list = new List<PlayerProfile>(_accountsById.Values);
                list.Sort((a, b) =>
                {
                    int cmp = b.EloRating.CompareTo(a.EloRating);
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
        /// Persiste en disco las cuentas de usuario y el historial de partidas concluidas.
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
                    sbAccounts.AppendLine($"{p.PlayerId}\t{p.Username}\t{p.EloRating}\t{p.Wins}\t{p.Losses}\t{p.Draws}");
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

                        var profile = new PlayerProfile(
                            cols[0],
                            cols[1],
                            int.TryParse(cols[2], out int elo) ? elo : EloRatingCalculator.DefaultInitialRating)
                        {
                            Wins = int.TryParse(cols[3], out int w) ? w : 0,
                            Losses = int.TryParse(cols[4], out int l) ? l : 0,
                            Draws = int.TryParse(cols[5], out int d) ? d : 0
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
                _completedMatches.Add(record);
                _activeRooms.Remove(room.RoomId);
            }
        }
    }
}
