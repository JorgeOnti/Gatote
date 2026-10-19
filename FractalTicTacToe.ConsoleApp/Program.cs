using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using FractalTicTacToe.Core;

namespace FractalTicTacToe.ConsoleApp
{
    public enum GameMode
    {
        HumanVsBotX = 1, // Humano (X) vs Bot (O)
        BotVsHumanO = 2, // Bot (X) vs Humano (O)
        HumanVsHuman = 3,
        BotVsBot = 4
    }

    public static class Program
    {
        private const string DefaultSaveFileName = "partida_fractal.fttt";

        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            GameMode mode = SelectGameMode(args, out MinimaxBot bot);
            CreateDefaultProfiles(mode, bot, out PlayerProfile profileX, out PlayerProfile profileO);

            var state = new GameState();
            string statusMessage = $"Modo activo: {GetModeDescription(mode, bot)}";
            bool isErrorMessage = false;
            bool eloAppliedForCurrentGame = false;
            EloMatchUpdate? lastEloUpdate = null;

            while (true)
            {
                if (state.IsGameOver && !eloAppliedForCurrentGame)
                {
                    EloMatchUpdate update = EloRatingCalculator.CalculateMatchRatingChange(
                        profileX.EloRating,
                        profileX.GamesPlayed,
                        profileO.EloRating,
                        profileO.GamesPlayed,
                        state.Winner);

                    profileX.ApplyMatchResult(Player.X, state.Winner, update.NewRatingX);
                    profileO.ApplyMatchResult(Player.O, state.Winner, update.NewRatingO);
                    lastEloUpdate = update;
                    eloAppliedForCurrentGame = true;
                }

                RenderScreen(state, statusMessage, isErrorMessage, mode, bot, profileX, profileO);
                statusMessage = null;
                isErrorMessage = false;

                if (state.IsGameOver)
                {
                    RenderGameOverBanner(state, profileX, profileO, lastEloUpdate);
                    Console.Write("\n¿Acción? (s = nueva partida / u = deshacer último tiro / e = exportar / m = modo / n = salir): ");
                    string retry = Console.ReadLine()?.Trim().ToLowerInvariant();

                    if (retry == "u" || retry == "undo")
                    {
                        if (PerformUndo(state, mode))
                        {
                            statusMessage = "Último movimiento deshecho. La partida continúa.";
                        }
                        continue;
                    }

                    if (retry == "e" || retry == "export")
                    {
                        statusMessage = ExportGameToDiskAndClipboardInfo(state, profileX, profileO);
                        continue;
                    }

                    if (retry == "m")
                    {
                        mode = SelectGameMode(Array.Empty<string>(), out bot);
                        CreateDefaultProfiles(mode, bot, out profileX, out profileO);
                        state.Reset();
                        eloAppliedForCurrentGame = false;
                        lastEloUpdate = null;
                        statusMessage = $"Modo cambiado a: {GetModeDescription(mode, bot)}";
                        continue;
                    }

                    if (retry == "s" || retry == "si" || retry == "sí" || retry == "y")
                    {
                        state.Reset();
                        bot.ClearTranspositionTable();
                        eloAppliedForCurrentGame = false;
                        lastEloUpdate = null;
                        continue;
                    }

                    Console.WriteLine("¡Gracias por jugar Fractal Tic-Tac-Toe!");
                    break;
                }

                bool isBotTurn = IsBotTurn(state.CurrentPlayer, mode);

                if (isBotTurn)
                {
                    Player botPlayer = state.CurrentPlayer;
                    ConsoleColor botColor = botPlayer == Player.X ? ConsoleColor.Red : ConsoleColor.Blue;
                    Console.WriteLine();
                    WriteColored($"  [Bot Minimax ({botPlayer})]", botColor);
                    Console.WriteLine(
                        $" Pensando (Dificultad: {bot.Difficulty}, Prof. máx: {bot.MaxDepth}, Límite: {bot.TimeLimitMs} ms)...");

                    MinimaxSearchResult searchResult = bot.FindBestMove(state);

                    if (mode == GameMode.BotVsBot)
                    {
                        Thread.Sleep(200);
                    }

                    state.MakeMove(searchResult.BestMove);
                    statusMessage =
                        $"Bot ({botPlayer}) jugó {searchResult.BestMove} | " +
                        $"Eval: {searchResult.Score} | Prof: {searchResult.MaxDepthReached}/{bot.MaxDepth} | " +
                        $"Nodos: {searchResult.NodesEvaluated:N0} | TT Hits: {searchResult.TranspositionHits:N0} ({searchResult.TranspositionCutoffs:N0} podas) | " +
                        $"{searchResult.ElapsedMs} ms";
                    continue;
                }

                RenderPrompt(state);
                string input = Console.ReadLine();

                if (input == null)
                {
                    break;
                }

                string trimmed = input.Trim();
                if (string.Equals(trimmed, "q", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "salir", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\nPartida finalizada por el usuario.");
                    break;
                }

                if (string.Equals(trimmed, "r", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "reiniciar", StringComparison.OrdinalIgnoreCase))
                {
                    state.Reset();
                    bot.ClearTranspositionTable();
                    eloAppliedForCurrentGame = false;
                    lastEloUpdate = null;
                    statusMessage = "Partida y Tabla de Transposición reiniciadas.";
                    continue;
                }

                if (string.Equals(trimmed, "u", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "undo", StringComparison.OrdinalIgnoreCase))
                {
                    if (PerformUndo(state, mode))
                    {
                        statusMessage = "Movimiento deshecho (Undo).";
                    }
                    else
                    {
                        statusMessage = "No hay movimientos previos para deshacer.";
                        isErrorMessage = true;
                    }
                    continue;
                }

                if (string.Equals(trimmed, "y", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "redo", StringComparison.OrdinalIgnoreCase))
                {
                    if (PerformRedo(state, mode))
                    {
                        statusMessage = "Movimiento rehecho (Redo).";
                    }
                    else
                    {
                        statusMessage = "No hay movimientos en la pila de Redo.";
                        isErrorMessage = true;
                    }
                    continue;
                }

                if (string.Equals(trimmed, "e", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "export", StringComparison.OrdinalIgnoreCase))
                {
                    statusMessage = ExportGameToDiskAndClipboardInfo(state, profileX, profileO);
                    continue;
                }

                if (string.Equals(trimmed, "i", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "import", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryImportGameInteractive(out GameState importedState, out string importMsg))
                    {
                        state = importedState;
                        bot.ClearTranspositionTable();
                        eloAppliedForCurrentGame = state.IsGameOver;
                        statusMessage = importMsg;
                    }
                    else
                    {
                        statusMessage = importMsg;
                        isErrorMessage = true;
                    }
                    continue;
                }

                if (string.Equals(trimmed, "m", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "modo", StringComparison.OrdinalIgnoreCase))
                {
                    mode = SelectGameMode(Array.Empty<string>(), out bot);
                    CreateDefaultProfiles(mode, bot, out profileX, out profileO);
                    state.Reset();
                    eloAppliedForCurrentGame = false;
                    lastEloUpdate = null;
                    statusMessage = $"Modo cambiado a: {GetModeDescription(mode, bot)}";
                    continue;
                }

                if (string.Equals(trimmed, "h", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "hint", StringComparison.OrdinalIgnoreCase))
                {
                    MinimaxSearchResult hint = bot.FindBestMove(state);
                    statusMessage =
                        $"Sugerencia IA: {hint.BestMove} | Eval: {hint.Score} | Prof: {hint.MaxDepthReached} | " +
                        $"Nodos: {hint.NodesEvaluated:N0} | TT Hits: {hint.TranspositionHits:N0} | {hint.ElapsedMs} ms";
                    continue;
                }

                if (!TryParseUserMove(trimmed, state, out Move move, out string parseError))
                {
                    statusMessage = parseError;
                    isErrorMessage = true;
                    continue;
                }

                if (!state.MakeMove(move))
                {
                    statusMessage = $"Movimiento inválido {move}. Asegúrate de elegir una casilla vacía dentro del tablero activo.";
                    isErrorMessage = true;
                    continue;
                }
            }
        }

        private static bool PerformUndo(GameState state, GameMode mode)
        {
            if (!state.CanUndo)
            {
                return false;
            }

            // En modo Humano vsHumano deshacemos 1 medio turno (ply).
            // En modo Humano vs Bot deshacemos hasta regresar al turno del jugador humano.
            state.UndoMove();
            if ((mode == GameMode.HumanVsBotX || mode == GameMode.BotVsHumanO) &&
                IsBotTurn(state.CurrentPlayer, mode) &&
                state.CanUndo)
            {
                state.UndoMove();
            }

            return true;
        }

        private static bool PerformRedo(GameState state, GameMode mode)
        {
            if (!state.CanRedo)
            {
                return false;
            }

            state.RedoMove();
            if ((mode == GameMode.HumanVsBotX || mode == GameMode.BotVsHumanO) &&
                IsBotTurn(state.CurrentPlayer, mode) &&
                state.CanRedo)
            {
                state.RedoMove();
            }

            return true;
        }

        private static string ExportGameToDiskAndClipboardInfo(
            GameState state,
            PlayerProfile profileX,
            PlayerProfile profileO)
        {
            string fen = GameSerializer.ToFen(state);
            string moves = GameSerializer.ExportMoveHistory(state);
            MatchRecord record = MatchRecord.CreateFromGame(state, profileX, profileO, updateProfilesIfGameOver: false);

            try
            {
                File.WriteAllText(DefaultSaveFileName, record.SerializeToText(), Encoding.UTF8);
                return $"Exportado a '{DefaultSaveFileName}' | FEN: \"{fen}\" | Movs: \"{(string.IsNullOrEmpty(moves) ? "(vacío)" : moves)}\"";
            }
            catch (Exception ex)
            {
                return $"FEN: \"{fen}\" | Movs: \"{moves}\" (No se pudo escribir archivo: {ex.Message})";
            }
        }

        private static bool TryImportGameInteractive(out GameState importedState, out string message)
        {
            importedState = null;
            Console.WriteLine();
            Console.Write($"  Ingresa cadena FEN, transcripción (ej. '01-10-02') o Enter para cargar '{DefaultSaveFileName}': ");
            string raw = Console.ReadLine()?.Trim();

            try
            {
                if (string.IsNullOrEmpty(raw))
                {
                    if (!File.Exists(DefaultSaveFileName))
                    {
                        message = $"No se encontró el archivo '{DefaultSaveFileName}'.";
                        return false;
                    }

                    string fileContent = File.ReadAllText(DefaultSaveFileName, Encoding.UTF8);
                    MatchRecord record = MatchRecord.DeserializeFromText(fileContent);
                    importedState = record.ReconstructGameState();
                    message = $"Partida cargada desde '{DefaultSaveFileName}' ({importedState.MoveCount} movimientos).";
                    return true;
                }

                // Si contiene '/', es una cadena FEN
                if (raw.Contains("/"))
                {
                    importedState = GameSerializer.FromFen(raw);
                    message = $"Posición importada desde FEN ({importedState.MoveCount} fichas en el tablero).";
                    return true;
                }

                // De lo contrario, se interpreta como transcripción de movimientos (ej. "01-10-02")
                importedState = GameSerializer.ImportMoveHistory(raw);
                message = $"Partida reconstruida con historial completo ({importedState.MoveCount} movimientos, Undo disponible).";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Error al importar partida: {ex.Message}";
                return false;
            }
        }

        private static void CreateDefaultProfiles(
            GameMode mode,
            MinimaxBot bot,
            out PlayerProfile profileX,
            out PlayerProfile profileO)
        {
            int botElo = GetBotInitialElo(bot.Difficulty);

            switch (mode)
            {
                case GameMode.HumanVsBotX:
                    profileX = new PlayerProfile("human-1", "Jugador Humano", 1200);
                    profileO = new PlayerProfile($"bot-{bot.Difficulty}", $"Bot Minimax ({bot.Difficulty})", botElo);
                    break;
                case GameMode.BotVsHumanO:
                    profileX = new PlayerProfile($"bot-{bot.Difficulty}", $"Bot Minimax ({bot.Difficulty})", botElo);
                    profileO = new PlayerProfile("human-1", "Jugador Humano", 1200);
                    break;
                case GameMode.BotVsBot:
                    profileX = new PlayerProfile("bot-x", $"Bot X ({bot.Difficulty})", botElo);
                    profileO = new PlayerProfile("bot-o", $"Bot O ({bot.Difficulty})", botElo);
                    break;
                default:
                    profileX = new PlayerProfile("human-x", "Jugador X", 1200);
                    profileO = new PlayerProfile("human-o", "Jugador O", 1200);
                    break;
            }
        }

        private static int GetBotInitialElo(BotDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BotDifficulty.Easy: return 1000;
                case BotDifficulty.Medium: return 1300;
                case BotDifficulty.Hard: return 1650;
                case BotDifficulty.Master: return 2050;
                default: return 1600;
            }
        }

        private static GameMode SelectGameMode(string[] args, out MinimaxBot bot)
        {
            if (Console.IsInputRedirected && (args == null || args.Length == 0))
            {
                bot = new MinimaxBot(BotDifficulty.Hard);
                return GameMode.HumanVsHuman;
            }

            try
            {
                Console.Clear();
            }
            catch
            {
                // Ignorar si la consola está redirigida
            }

            WriteColoredLine("╔══════════════════════════════════════════════════════════════════════╗", ConsoleColor.Cyan);
            WriteColoredLine("║          FRACTAL TIC-TAC-TOE  •  SELECCIÓN DE MODO DE JUEGO         ║", ConsoleColor.Cyan);
            WriteColoredLine("╚══════════════════════════════════════════════════════════════════════╝", ConsoleColor.Cyan);
            Console.WriteLine();
            Console.WriteLine("  [1] Humano (X) vs. Bot Minimax (O)  [Recomendado]");
            Console.WriteLine("  [2] Bot Minimax (X) vs. Humano (O)");
            Console.WriteLine("  [3] Humano (X) vs. Humano (O)");
            Console.WriteLine("  [4] Bot Minimax (X) vs. Bot Minimax (O)  [Simulación]");
            Console.WriteLine();
            Console.Write("  Selecciona una opción (1-4) [Por defecto: 1]: ");

            string choice = Console.ReadLine()?.Trim();
            GameMode selectedMode = GameMode.HumanVsBotX;

            if (choice == "2")
            {
                selectedMode = GameMode.BotVsHumanO;
            }
            else if (choice == "3")
            {
                selectedMode = GameMode.HumanVsHuman;
            }
            else if (choice == "4")
            {
                selectedMode = GameMode.BotVsBot;
            }

            if (selectedMode == GameMode.HumanVsHuman)
            {
                bot = new MinimaxBot(BotDifficulty.Hard);
                return selectedMode;
            }

            Console.WriteLine();
            Console.WriteLine("  Niveles de Dificultad del Bot:");
            Console.WriteLine("    [1] Fácil      (Prof. 2 |  100 ms | ~1000 ELO)");
            Console.WriteLine("    [2] Medio      (Prof. 4 |  350 ms | ~1300 ELO)");
            Console.WriteLine("    [3] Difícil    (Prof. 6 | 1000 ms | ~1650 ELO) [Por defecto]");
            Console.WriteLine("    [4] Maestro    (Prof. 9 | 2500 ms | ~2050 ELO)");
            Console.WriteLine("    [5] Personalizado (Definir profundidad y límite de tiempo en ms)");
            Console.Write("  Selecciona dificultad (1-5) [Por defecto: 3]: ");

            string diffInput = Console.ReadLine()?.Trim();
            switch (diffInput)
            {
                case "1":
                    bot = new MinimaxBot(BotDifficulty.Easy);
                    break;
                case "2":
                    bot = new MinimaxBot(BotDifficulty.Medium);
                    break;
                case "4":
                    bot = new MinimaxBot(BotDifficulty.Master);
                    break;
                case "5":
                    Console.Write("    Profundidad máxima (1-12) [Por defecto: 7]: ");
                    int customDepth = int.TryParse(Console.ReadLine()?.Trim(), out int d) && d >= 1 && d <= 12 ? d : 7;
                    Console.Write("    Tiempo límite por jugada en ms (50-10000) [Por defecto: 1500]: ");
                    int customTime = int.TryParse(Console.ReadLine()?.Trim(), out int t) && t >= 50 && t <= 10000 ? t : 1500;
                    bot = new MinimaxBot(customDepth, customTime, useTranspositionTable: true);
                    break;
                default:
                    bot = new MinimaxBot(BotDifficulty.Hard);
                    break;
            }

            return selectedMode;
        }

        private static bool IsBotTurn(Player currentPlayer, GameMode mode)
        {
            switch (mode)
            {
                case GameMode.HumanVsBotX:
                    return currentPlayer == Player.O;
                case GameMode.BotVsHumanO:
                    return currentPlayer == Player.X;
                case GameMode.BotVsBot:
                    return true;
                default:
                    return false;
            }
        }

        private static string GetModeDescription(GameMode mode, MinimaxBot bot)
        {
            string botInfo = $"{bot.Difficulty} (Prof. máx {bot.MaxDepth}, {bot.TimeLimitMs} ms)";
            switch (mode)
            {
                case GameMode.HumanVsBotX:
                    return $"Humano (X) vs. Bot (O) [{botInfo}]";
                case GameMode.BotVsHumanO:
                    return $"Bot (X) [{botInfo}] vs. Humano (O)";
                case GameMode.BotVsBot:
                    return $"Bot (X) vs. Bot (O) [{botInfo}]";
                default:
                    return "Humano (X) vs. Humano (O)";
            }
        }

        private static bool TryParseUserMove(string input, GameState state, out Move move, out string error)
        {
            move = default;
            error = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Por favor ingresa las coordenadas de tu tiro.";
                return false;
            }

            string[] parts = input.Split(new[] { ' ', ',', '-', ':', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1 && state.ActiveBoardIndex != GameState.FreeMoveBoardIndex)
            {
                if (!int.TryParse(parts[0], out int cellIndex) || cellIndex < 0 || cellIndex > 8)
                {
                    error = "El índice de la casilla debe ser un número entero entre 0 y 8.";
                    return false;
                }

                move = new Move(state.ActiveBoardIndex, cellIndex);
                return true;
            }

            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[0], out int boardIndex) || boardIndex < 0 || boardIndex > 8)
                {
                    error = "El índice del tablero macro debe ser un número entero entre 0 y 8.";
                    return false;
                }

                if (!int.TryParse(parts[1], out int cellIndex) || cellIndex < 0 || cellIndex > 8)
                {
                    error = "El índice de la casilla debe ser un número entero entre 0 y 8.";
                    return false;
                }

                if (state.ActiveBoardIndex != GameState.FreeMoveBoardIndex && boardIndex != state.ActiveBoardIndex)
                {
                    error = $"Debes jugar obligatoriamente en el Tablero Macro #{state.ActiveBoardIndex}.";
                    return false;
                }

                move = new Move(boardIndex, cellIndex);
                return true;
            }

            error = state.ActiveBoardIndex == GameState.FreeMoveBoardIndex
                ? "Tiro libre activo: ingresa 'Tablero Celda' separados por espacio (ej. '4 0')."
                : $"Ingresa la 'Celda' (0-8) o '{state.ActiveBoardIndex} Celda' (ej. '4' o '{state.ActiveBoardIndex} 4').";
            return false;
        }

        private static void RenderScreen(
            GameState state,
            string statusMessage,
            bool isErrorMessage,
            GameMode mode,
            MinimaxBot bot,
            PlayerProfile profileX,
            PlayerProfile profileO)
        {
            try
            {
                Console.Clear();
            }
            catch
            {
                // Ignorar si la consola está redirigida
            }

            Move? lastMove = state.GetLastMove();

            WriteColoredLine("╔══════════════════════════════════════════════════════════════════════╗", ConsoleColor.Cyan);
            WriteColoredLine("║          FRACTAL TIC-TAC-TOE  •  MODO MUERTE SÚBITA (3 EN RAYA)     ║", ConsoleColor.Cyan);
            WriteColoredLine("╚══════════════════════════════════════════════════════════════════════╝", ConsoleColor.Cyan);

            Console.Write("  [X] ");
            WriteColored($"{profileX}", ConsoleColor.Red);
            Console.Write("   vs.   [O] ");
            WriteColoredLine($"{profileO}", ConsoleColor.Blue);

            Console.Write("  Turno: ");
            if (state.IsGameOver)
            {
                WriteColored("PARTIDA TERMINADA", ConsoleColor.Yellow);
            }
            else
            {
                ConsoleColor playerColor = state.CurrentPlayer == Player.X ? ConsoleColor.Red : ConsoleColor.Blue;
                WriteColored($"Jugador {state.CurrentPlayer}", playerColor);
            }

            Console.Write($"   |   Movs: {state.MoveCount}/81 (Redo: {state.RedoCount})   |   Último: ");
            if (lastMove.HasValue)
            {
                WriteColoredLine($"B:{lastMove.Value.BoardIndex}, C:{lastMove.Value.CellIndex}", ConsoleColor.White);
            }
            else
            {
                Console.WriteLine("Ninguno");
            }

            Console.Write("  Restricción actual: ");
            if (state.IsGameOver)
            {
                Console.WriteLine("Ninguna (Juego finalizado)");
            }
            else if (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex)
            {
                WriteColoredLine("TIRO LIBRE (Puedes elegir cualquier Tablero 0..8 con casillas disponibles)", ConsoleColor.Green);
            }
            else
            {
                WriteColoredLine($"TABLERO FORZADO #{state.ActiveBoardIndex} (Resaltado en verde)", ConsoleColor.Green);
            }

            Console.WriteLine();
            RenderFractalBoard(state, lastMove);

            if (!string.IsNullOrEmpty(statusMessage))
            {
                Console.WriteLine();
                WriteColoredLine($"  >> {statusMessage}", isErrorMessage ? ConsoleColor.Red : ConsoleColor.Yellow);
            }
        }

        private static void RenderFractalBoard(GameState state, Move? lastMove)
        {
            string[] guideLines =
            {
                "Guía de Índices (0..8):",
                " ┌───────────┐",
                " │ 0 │ 1 │ 2 │",
                " ├───┼───┼───┤",
                " │ 3 │ 4 │ 5 │",
                " ├───┼───┼───┤",
                " │ 6 │ 7 │ 8 │",
                " └───────────┘",
                "",
                "Comandos rápidos:",
                " • 'u' = Undo (deshacer) | 'y' = Redo (rehacer)",
                " • 'e' = Exportar FEN/PGN| 'i' = Importar partida",
                " • 'h' = Pista Minimax   | 'm' = Modo/Dificultad",
                " • 'r' = Reiniciar       | 'q' = Salir"
            };

            int guideLineIndex = 0;

            for (int macroRow = 0; macroRow < 3; macroRow++)
            {
                Console.Write("  ");
                for (int macroCol = 0; macroCol < 3; macroCol++)
                {
                    int boardIndex = (macroRow * 3) + macroCol;
                    bool isBoardPlayable = !state.IsGameOver &&
                                           (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex ||
                                            state.ActiveBoardIndex == boardIndex);

                    ConsoleColor headerColor = isBoardPlayable ? ConsoleColor.Green : ConsoleColor.DarkGray;
                    string marker = state.ActiveBoardIndex == boardIndex && !state.IsGameOver ? "★" : " ";
                    WriteColored($"  {marker} TABLERO {boardIndex} {marker}  ", headerColor);

                    if (macroCol < 2)
                    {
                        Console.Write("   ");
                    }
                }
                AppendGuideLine(guideLines, ref guideLineIndex);

                for (int cellRow = 0; cellRow < 3; cellRow++)
                {
                    Console.Write("  ");
                    for (int macroCol = 0; macroCol < 3; macroCol++)
                    {
                        int boardIndex = (macroRow * 3) + macroCol;
                        bool isBoardPlayable = !state.IsGameOver &&
                                               (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex ||
                                                state.ActiveBoardIndex == boardIndex);

                        ConsoleColor borderColor = isBoardPlayable ? ConsoleColor.Green : ConsoleColor.DarkGray;
                        WriteColored("│", borderColor);

                        for (int cellCol = 0; cellCol < 3; cellCol++)
                        {
                            int cellIndex = (cellRow * 3) + cellCol;
                            Player occupant = state.GetCell(boardIndex, cellIndex);
                            bool isLastMove = lastMove.HasValue &&
                                              lastMove.Value.BoardIndex == boardIndex &&
                                              lastMove.Value.CellIndex == cellIndex;

                            RenderSingleCell(occupant, cellIndex, isBoardPlayable, isLastMove);

                            if (cellCol < 2)
                            {
                                WriteColored("·", borderColor);
                            }
                        }

                        WriteColored("│", borderColor);

                        if (macroCol < 2)
                        {
                            WriteColored(" ║ ", ConsoleColor.White);
                        }
                    }

                    AppendGuideLine(guideLines, ref guideLineIndex);
                }

                if (macroRow < 2)
                {
                    Console.Write("  ");
                    WriteColored("═════════════════╬═══════════════════╬═════════════════", ConsoleColor.White);
                    AppendGuideLine(guideLines, ref guideLineIndex);
                }
            }
        }

        private static void RenderSingleCell(Player occupant, int cellIndex, bool isBoardPlayable, bool isLastMove)
        {
            if (occupant == Player.X)
            {
                WriteColored(isLastMove ? "[X]" : " X ", isLastMove ? ConsoleColor.Magenta : ConsoleColor.Red);
            }
            else if (occupant == Player.O)
            {
                WriteColored(isLastMove ? "[O]" : " O ", isLastMove ? ConsoleColor.Cyan : ConsoleColor.Blue);
            }
            else
            {
                if (isBoardPlayable)
                {
                    WriteColored($" {cellIndex} ", ConsoleColor.DarkGray);
                }
                else
                {
                    WriteColored(" . ", ConsoleColor.DarkGray);
                }
            }
        }

        private static void AppendGuideLine(string[] guideLines, ref int index)
        {
            if (index < guideLines.Length)
            {
                Console.Write("    ");
                WriteColoredLine(guideLines[index], ConsoleColor.Gray);
                index++;
            }
            else
            {
                Console.WriteLine();
            }
        }

        private static void RenderPrompt(GameState state)
        {
            Console.WriteLine();
            List<Move> legalMoves = state.GetLegalMoves();
            Console.WriteLine($"  Movimientos legales disponibles: {legalMoves.Count}");

            ConsoleColor playerColor = state.CurrentPlayer == Player.X ? ConsoleColor.Red : ConsoleColor.Blue;
            WriteColored($"  [Jugador {state.CurrentPlayer}] ", playerColor);

            if (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex)
            {
                Console.Write("Ingresa 'Tablero Celda' (0-8 0-8) o comando (u/y/e/i/h): ");
            }
            else
            {
                Console.Write($"Tablero #{state.ActiveBoardIndex} activo -> Ingresa 'Celda' (0-8) o comando (u/y/e/i/h): ");
            }
        }

        private static void RenderGameOverBanner(
            GameState state,
            PlayerProfile profileX,
            PlayerProfile profileO,
            EloMatchUpdate? eloUpdate)
        {
            Console.WriteLine();
            WriteColoredLine("  ╔══════════════════════════════════════════════════════╗", ConsoleColor.Yellow);
            if (state.Winner != Player.None)
            {
                ConsoleColor winnerColor = state.Winner == Player.X ? ConsoleColor.Red : ConsoleColor.Blue;
                Console.Write("  ║   ¡VICTORIA POR MUERTE SÚBITA! GANADOR: ");
                WriteColored($"JUGADOR {state.Winner}", winnerColor);
                WriteColoredLine("    ║", ConsoleColor.Yellow);
            }
            else
            {
                WriteColoredLine("  ║     ¡EMPATE GLOBAL! Las 81 casillas están llenas.   ║", ConsoleColor.Yellow);
            }
            WriteColoredLine("  ╚══════════════════════════════════════════════════════╝", ConsoleColor.Yellow);

            if (eloUpdate.HasValue)
            {
                string signX = eloUpdate.Value.DeltaX >= 0 ? "+" : "";
                string signO = eloUpdate.Value.DeltaO >= 0 ? "+" : "";
                Console.WriteLine(
                    $"  Rating ELO actualizado -> [X] {profileX.Username}: {eloUpdate.Value.NewRatingX} ({signX}{eloUpdate.Value.DeltaX}) | " +
                    $"[O] {profileO.Username}: {eloUpdate.Value.NewRatingO} ({signO}{eloUpdate.Value.DeltaO})");
            }
        }

        private static void WriteColored(string text, ConsoleColor color)
        {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = previous;
        }

        private static void WriteColoredLine(string text, ConsoleColor color)
        {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = previous;
        }
    }
}
