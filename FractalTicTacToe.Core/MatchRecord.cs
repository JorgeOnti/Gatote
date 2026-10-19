using System;
using System.Collections.Generic;
using System.Globalization;
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
        DisconnectForfeit = 5
    }

    /// <summary>
    /// Perfil de cuenta de jugador con estadísticas acumuladas y rating ELO tipo ajedrez.
    /// Diseñado para persistencia local o base de datos de cuentas en línea.
    /// </summary>
    public sealed class PlayerProfile
    {
        public string PlayerId { get; set; }
        public string Username { get; set; }
        public int EloRating { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }

        public int GamesPlayed => Wins + Losses + Draws;
        public string RankTitle => EloRatingCalculator.GetRankTitle(EloRating);

        public PlayerProfile(string playerId, string username, int initialElo = EloRatingCalculator.DefaultInitialRating)
        {
            PlayerId = string.IsNullOrWhiteSpace(playerId) ? Guid.NewGuid().ToString("N") : playerId;
            Username = string.IsNullOrWhiteSpace(username) ? "Jugador" : username;
            EloRating = initialElo;
        }

        public void ApplyMatchResult(Player assignedRole, Player winner, int newEloRating)
        {
            EloRating = newEloRating;
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
            $"{Username} [{EloRating} ELO - {RankTitle}] ({Wins}V/{Losses}D/{Draws}E)";
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
