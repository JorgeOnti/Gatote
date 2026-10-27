using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Motivo de conclusión de una partida (local o en línea).
    /// </summary>
    public enum MatchEndReason
    {
        InProgress = 0,
        SuddenDeathThreeInARow = 1,
        GlobalDraw = 2,
        Resignation = 3,
        Timeout = 4,
        DisconnectForfeit = 5,
        Aborted = 6
    }

    /// <summary>
    /// Filtro temporal para consultar el historial de partidas jugadas en la pantalla de Análisis.
    /// </summary>
    public enum MatchDateFilter
    {
        All = 0,
        Today = 1,
        LastWeek = 2,
        LastMonth = 3
    }

    /// <summary>
    /// Perfil de cuenta de jugador con estadísticas acumuladas, credenciales autenticadas (PBKDF2-SHA256)
    /// y puntaje competitivo desglosado por modo de juego (Bullet, Blitz y Rápida).
    /// Diseñado para persistencia local o base de datos de cuentas en línea.
    /// </summary>
    public sealed class PlayerProfile
    {
        private const int Pbkdf2Iterations = 50_000;
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        private const string Pbkdf2Prefix = "pbkdf2$";

        private int _eloRating;

        public string PlayerId { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public int EloRating
        {
            get => _eloRating;
            set
            {
                if (BulletElo == _eloRating && BlitzElo == _eloRating && RapidElo == _eloRating)
                {
                    BulletElo = value;
                    BlitzElo = value;
                    RapidElo = value;
                }
                _eloRating = value;
            }
        }
        public int BulletElo { get; set; }
        public int BlitzElo { get; set; }
        public int RapidElo { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }

        public int GamesPlayed => Wins + Losses + Draws;
        public double WinRatePercentage => GamesPlayed == 0 ? 0.0 : (Wins * 100.0) / GamesPlayed;
        public string RankTitle => EloRatingCalculator.GetRankTitle(EloRating);

        public PlayerProfile(string playerId, string username, int initialElo = EloRatingCalculator.DefaultInitialRating)
        {
            PlayerId = string.IsNullOrWhiteSpace(playerId) ? Guid.NewGuid().ToString("N") : playerId;
            Username = string.IsNullOrWhiteSpace(username) ? "Jugador" : username;
            _eloRating = initialElo;
            BulletElo = initialElo;
            BlitzElo = initialElo;
            RapidElo = initialElo;
        }

        public int GetRatingForTimeControl(TimeControlConfig timeControl)
        {
            switch (timeControl.Category)
            {
                case TimeControlCategory.Bullet:
                    return BulletElo;
                case TimeControlCategory.Blitz:
                    return BlitzElo;
                case TimeControlCategory.Rapid:
                    return RapidElo;
                default:
                    return _eloRating;
            }
        }

        public void SetRatingForTimeControl(TimeControlConfig timeControl, int newRating)
        {
            switch (timeControl.Category)
            {
                case TimeControlCategory.Bullet:
                    BulletElo = newRating;
                    break;
                case TimeControlCategory.Blitz:
                    BlitzElo = newRating;
                    break;
                case TimeControlCategory.Rapid:
                    RapidElo = newRating;
                    break;
            }
            _eloRating = newRating;
        }

        public void SetPassword(string plainPassword)
        {
            PasswordHash = ComputeSecurePbkdf2Hash(plainPassword);
        }

        public bool VerifyPassword(string plainPassword)
        {
            if (string.IsNullOrEmpty(PasswordHash))
            {
                return true;
            }

            string candidate = plainPassword ?? string.Empty;
            if (PasswordHash.StartsWith(Pbkdf2Prefix, StringComparison.Ordinal))
            {
                return VerifyPbkdf2Hash(candidate, PasswordHash);
            }

            // Compatibilidad hacia atrás con hashes SHA-256 previos en accounts.tsv
            string legacyHash = ComputePasswordHash(candidate);
            return ConstantTimeStringEquals(PasswordHash, legacyHash);
        }

        public static string ComputeSecurePbkdf2Hash(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
            {
                return string.Empty;
            }

            byte[] salt = new byte[SaltSizeBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            using (var pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
            {
                byte[] subkey = pbkdf2.GetBytes(HashSizeBytes);
                return $"{Pbkdf2Prefix}{Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}";
            }
        }

        private static bool VerifyPbkdf2Hash(string plainPassword, string storedHash)
        {
            if (string.IsNullOrEmpty(plainPassword) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            string[] parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations) || iterations < 1000)
            {
                return false;
            }

            try
            {
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expectedSubkey = Convert.FromBase64String(parts[3]);
                using (var pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, iterations, HashAlgorithmName.SHA256))
                {
                    byte[] actualSubkey = pbkdf2.GetBytes(expectedSubkey.Length);
                    return ConstantTimeBytesEquals(expectedSubkey, actualSubkey);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool ConstantTimeBytesEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }

        private static bool ConstantTimeStringEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            byte[] ba = Encoding.UTF8.GetBytes(a.ToLowerInvariant());
            byte[] bb = Encoding.UTF8.GetBytes(b.ToLowerInvariant());
            return ConstantTimeBytesEquals(ba, bb);
        }

        public static string ComputePasswordHash(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
            {
                return string.Empty;
            }

            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes("GatoteFractalSalt::" + plainPassword);
                byte[] hash = sha256.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        public void ApplyMatchResult(Player assignedRole, Player winner, int newEloRating)
        {
            ApplyMatchResultForTimeControl(TimeControlConfig.Blitz3Plus2, assignedRole, winner, newEloRating);
        }

        public void ApplyMatchResultForTimeControl(TimeControlConfig timeControl, Player assignedRole, Player winner, int newModeRating)
        {
            SetRatingForTimeControl(timeControl, newModeRating);
            if (winner == Player.None)
            {
                Draws++;
            }
            else if (winner == assignedRole)
            {
                Wins++;
            }
            else
            {
                Losses++;
            }
        }

        public override string ToString() =>
            $"{Username} [{EloRating} ELO (B:{BulletElo}/Z:{BlitzElo}/R:{RapidElo}) - {RankTitle}] ({Wins}V/{Losses}D/{Draws}E)";
    }

    /// <summary>
    /// Registro completo y auditable de una partida (metadatos de cuentas, ELO antes/después,
    /// estado FEN final e historial completo de movimientos).
    /// Soporta exportación/importación en formato PGN de texto plano sin dependencias externas.
    /// </summary>
    public sealed class MatchRecord
    {
        public string MatchId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime PlayedAtUtc { get; set; } = DateTime.UtcNow;
        public string PlayerXName { get; set; } = "Jugador X";
        public string PlayerOName { get; set; } = "Jugador O";
        public int PlayerXEloBefore { get; set; } = EloRatingCalculator.DefaultInitialRating;
        public int PlayerOEloBefore { get; set; } = EloRatingCalculator.DefaultInitialRating;
        public int PlayerXEloAfter { get; set; } = EloRatingCalculator.DefaultInitialRating;
        public int PlayerOEloAfter { get; set; } = EloRatingCalculator.DefaultInitialRating;
        public Player Winner { get; set; } = Player.None;
        public MatchEndReason EndReason { get; set; } = MatchEndReason.InProgress;
        public string FinalFen { get; set; } = string.Empty;
        public string MoveTranscript { get; set; } = string.Empty;

        /// <summary>
        /// Construye un <see cref="MatchRecord"/> a partir de un <see cref="GameState"/> y los perfiles de ambos jugadores.
        /// Si la partida ha concluido, calcula automáticamente el ajuste ELO.
        /// </summary>
        public static MatchRecord CreateFromGame(
            GameState state,
            PlayerProfile playerX,
            PlayerProfile playerO,
            bool updateProfilesIfGameOver = false)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (playerX == null) throw new ArgumentNullException(nameof(playerX));
            if (playerO == null) throw new ArgumentNullException(nameof(playerO));

            MatchEndReason reason = MatchEndReason.InProgress;
            int eloXBefore = playerX.EloRating;
            int eloOBefore = playerO.EloRating;
            int eloXAfter = eloXBefore;
            int eloOAfter = eloOBefore;

            if (state.IsGameOver)
            {
                reason = state.Winner != Player.None
                    ? MatchEndReason.SuddenDeathThreeInARow
                    : MatchEndReason.GlobalDraw;

                EloMatchUpdate update = EloRatingCalculator.CalculateMatchRatingChange(
                    eloXBefore,
                    playerX.GamesPlayed,
                    eloOBefore,
                    playerO.GamesPlayed,
                    state.Winner);

                eloXAfter = update.NewRatingX;
                eloOAfter = update.NewRatingO;

                if (updateProfilesIfGameOver)
                {
                    playerX.ApplyMatchResult(Player.X, state.Winner, eloXAfter);
                    playerO.ApplyMatchResult(Player.O, state.Winner, eloOAfter);
                }
            }

            return new MatchRecord
            {
                MatchId = Guid.NewGuid().ToString("N"),
                PlayedAtUtc = DateTime.UtcNow,
                PlayerXName = playerX.Username,
                PlayerOName = playerO.Username,
                PlayerXEloBefore = eloXBefore,
                PlayerOEloBefore = eloOBefore,
                PlayerXEloAfter = eloXAfter,
                PlayerOEloAfter = eloOAfter,
                Winner = state.Winner,
                EndReason = reason,
                FinalFen = GameSerializer.ToFen(state),
                MoveTranscript = GameSerializer.ExportMoveHistory(state)
            };
        }

        /// <summary>
        /// Reconstruye el <see cref="GameState"/> completo de esta partida.
        /// Si cuenta con <see cref="MoveTranscript"/>, reproduce todos los tiros para conservar el historial Undo/Redo;
        /// de lo contrario reconstruye la posición desde <see cref="FinalFen"/>.
        /// </summary>
        public GameState ReconstructGameState()
        {
            if (!string.IsNullOrWhiteSpace(MoveTranscript))
            {
                return GameSerializer.ImportMoveHistory(MoveTranscript);
            }

            if (!string.IsNullOrWhiteSpace(FinalFen))
            {
                return GameSerializer.FromFen(FinalFen);
            }

            return new GameState();
        }

        /// <summary>
        /// Serializa el registro completo de la partida en un formato portable inspirado en PGN de ajedrez.
        /// </summary>
        public string SerializeToText()
        {
            var sb = new StringBuilder(256);
            sb.AppendLine($"[MatchId \"{MatchId}\"]");
            sb.AppendLine($"[DateUtc \"{PlayedAtUtc.ToString("O", CultureInfo.InvariantCulture)}\"]");
            sb.AppendLine($"[PlayerX \"{EscapeTag(PlayerXName)}\"]");
            sb.AppendLine($"[PlayerO \"{EscapeTag(PlayerOName)}\"]");
            sb.AppendLine($"[EloX \"{PlayerXEloBefore}->{PlayerXEloAfter}\"]");
            sb.AppendLine($"[EloO \"{PlayerOEloBefore}->{PlayerOEloAfter}\"]");
            sb.AppendLine($"[Winner \"{Winner}\"]");
            sb.AppendLine($"[EndReason \"{EndReason}\"]");
            sb.AppendLine($"[FEN \"{FinalFen}\"]");
            sb.AppendLine($"[Moves \"{MoveTranscript}\"]");
            return sb.ToString();
        }

        /// <summary>
        /// Deserializa un <see cref="MatchRecord"/> desde su representación textual PGN.
        /// </summary>
        public static MatchRecord DeserializeFromText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("El contenido del registro de partida está vacío.", nameof(content));
            }

            var record = new MatchRecord();
            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("[") || !line.EndsWith("]"))
                {
                    continue;
                }

                int firstSpace = line.IndexOf(' ');
                int firstQuote = line.IndexOf('"');
                int lastQuote = line.LastIndexOf('"');

                if (firstSpace < 0 || firstQuote < 0 || lastQuote <= firstQuote)
                {
                    continue;
                }

                string key = line.Substring(1, firstSpace - 1).Trim();
                string value = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);

                switch (key)
                {
                    case "MatchId":
                        record.MatchId = value;
                        break;
                    case "DateUtc":
                        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dt))
                        {
                            record.PlayedAtUtc = dt;
                        }
                        break;
                    case "PlayerX":
                        record.PlayerXName = value;
                        break;
                    case "PlayerO":
                        record.PlayerOName = value;
                        break;
                    case "EloX":
                        ParseEloTransition(value, out int xBefore, out int xAfter);
                        record.PlayerXEloBefore = xBefore;
                        record.PlayerXEloAfter = xAfter;
                        break;
                    case "EloO":
                        ParseEloTransition(value, out int oBefore, out int oAfter);
                        record.PlayerOEloBefore = oBefore;
                        record.PlayerOEloAfter = oAfter;
                        break;
                    case "Winner":
                        if (Enum.TryParse(value, ignoreCase: true, out Player parsedWinner))
                        {
                            record.Winner = parsedWinner;
                        }
                        break;
                    case "EndReason":
                        if (Enum.TryParse(value, ignoreCase: true, out MatchEndReason parsedReason))
                        {
                            record.EndReason = parsedReason;
                        }
                        break;
                    case "FEN":
                        record.FinalFen = value;
                        break;
                    case "Moves":
                        record.MoveTranscript = value;
                        break;
                }
            }

            return record;
        }

        /// <summary>
        /// Filtra una colección de partidas según el rango temporal elegido (<see cref="MatchDateFilter"/>).
        /// </summary>
        public static List<MatchRecord> FilterByDate(
            IEnumerable<MatchRecord> records,
            MatchDateFilter filter,
            DateTime? referenceUtc = null)
        {
            var result = new List<MatchRecord>();
            if (records == null)
            {
                return result;
            }

            DateTime nowUtc = (referenceUtc ?? DateTime.UtcNow).ToUniversalTime();
            DateTime nowLocal = nowUtc.ToLocalTime();

            foreach (MatchRecord rec in records)
            {
                if (rec == null) continue;

                if (filter == MatchDateFilter.All)
                {
                    result.Add(rec);
                    continue;
                }

                DateTime playedUtc = rec.PlayedAtUtc.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(rec.PlayedAtUtc, DateTimeKind.Utc)
                    : rec.PlayedAtUtc.ToUniversalTime();
                DateTime playedLocal = playedUtc.ToLocalTime();
                TimeSpan age = nowUtc - playedUtc;

                switch (filter)
                {
                    case MatchDateFilter.Today:
                        if (playedLocal.Date == nowLocal.Date || (age >= TimeSpan.Zero && age.TotalHours <= 24.0))
                        {
                            result.Add(rec);
                        }
                        break;
                    case MatchDateFilter.LastWeek:
                        if (age.TotalDays <= 7.0 && age.TotalDays >= -1.0)
                        {
                            result.Add(rec);
                        }
                        break;
                    case MatchDateFilter.LastMonth:
                        if (age.TotalDays <= 30.0 && age.TotalDays >= -1.0)
                        {
                            result.Add(rec);
                        }
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Guarda en disco el historial de partidas jugadas para que persista al cerrar y volver a abrir la app.
        /// </summary>
        public static void SaveHistoryToFile(string filePath, IEnumerable<MatchRecord> records)
        {
            if (string.IsNullOrWhiteSpace(filePath) || records == null)
            {
                return;
            }

            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sb = new StringBuilder();
                foreach (MatchRecord r in records)
                {
                    if (r == null) continue;
                    sb.AppendLine(r.SerializeToText().Trim());
                    sb.AppendLine("---");
                }
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Ignorar errores de E/S en almacenamiento restringido
            }
        }

        /// <summary>
        /// Carga desde disco el historial de partidas jugadas previamente.
        /// </summary>
        public static List<MatchRecord> LoadHistoryFromFile(string filePath)
        {
            var result = new List<MatchRecord>();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return result;
            }

            try
            {
                string content = File.ReadAllText(filePath, Encoding.UTF8);
                string[] blocks = content.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string block in blocks)
                {
                    if (string.IsNullOrWhiteSpace(block)) continue;
                    try
                    {
                        MatchRecord rec = DeserializeFromText(block);
                        if (rec != null)
                        {
                            result.Add(rec);
                        }
                    }
                    catch
                    {
                        // Ignorar bloque corrupto individual
                    }
                }
            }
            catch
            {
                // Ignorar error de lectura
            }

            return result;
        }

        private static void ParseEloTransition(string text, out int before, out int after)
        {
            before = EloRatingCalculator.DefaultInitialRating;
            after = EloRatingCalculator.DefaultInitialRating;

            string[] parts = text.Split(new[] { "->" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out before);
                int.TryParse(parts[1], out after);
            }
            else if (parts.Length == 1 && int.TryParse(parts[0], out int single))
            {
                before = single;
                after = single;
            }
        }

        private static string EscapeTag(string input)
        {
            return (input ?? string.Empty).Replace("\"", "'");
        }
    }
}
