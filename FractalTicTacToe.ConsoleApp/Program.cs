using System;
using System.Collections.Generic;
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
        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            GameMode mode = SelectGameMode(args, out MinimaxBot bot);

            var state = new GameState();
            Move? lastMove = null;
            string statusMessage = $"Modo activo: {GetModeDescription(mode, bot)}";
            bool isErrorMessage = false;

            while (true)
            {
                RenderScreen(state, lastMove, statusMessage, isErrorMessage, mode, bot);
                statusMessage = null;
                isErrorMessage = false;

                if (state.IsGameOver)
                {
                    RenderGameOverBanner(state);
                    Console.Write("\n¿Desean jugar otra partida? (s = sí / m = cambiar modo / n = salir): ");
                    string retry = Console.ReadLine()?.Trim().ToLowerInvariant();

                    if (retry == "m")
                    {
                        mode = SelectGameMode(Array.Empty<string>(), out bot);
                        state = new GameState();
                        lastMove = null;
                        statusMessage = $"Modo cambiado a: {GetModeDescription(mode, bot)}";
                        continue;
                    }

                    if (retry == "s" || retry == "si" || retry == "sí" || retry == "y")
                    {
                        state = new GameState();
                        bot.ClearTranspositionTable();
                        lastMove = null;
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
                    lastMove = searchResult.BestMove;
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
                    state = new GameState();
                    bot.ClearTranspositionTable();
                    lastMove = null;
                    statusMessage = "Partida y Tabla de Transposición reiniciadas.";
                    continue;
                }

                if (string.Equals(trimmed, "m", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "modo", StringComparison.OrdinalIgnoreCase))
                {
                    mode = SelectGameMode(Array.Empty<string>(), out bot);
                    state = new GameState();
                    lastMove = null;
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

                lastMove = move;
            }
        }

        private static GameMode SelectGameMode(string[] args, out MinimaxBot bot)
        {
            // Si la entrada estándar está redirigida (ej. pruebas automatizadas por pipe), mantener 2P por defecto
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
            Console.WriteLine("    [1] Fácil      (Prof. 2 |  100 ms | Sin Tabla Zobrist)");
            Console.WriteLine("    [2] Medio      (Prof. 4 |  350 ms | Con Tabla Zobrist)");
            Console.WriteLine("    [3] Difícil    (Prof. 6 | 1000 ms | Iterative Deepening + Zobrist) [Por defecto]");
            Console.WriteLine("    [4] Maestro    (Prof. 9 | 2500 ms | Iterative Deepening + Zobrist)");
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
            string botInfo = $"{bot.Difficulty} (Prof. máx {bot.MaxDepth}, {bot.TimeLimitMs} ms, TT={(bot.UseTranspositionTable ? "ON" : "OFF")})";
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
            Move? lastMove,
            string statusMessage,
            bool isErrorMessage,
            GameMode mode,
            MinimaxBot bot)
        {
            try
            {
                Console.Clear();
            }
            catch
            {
                // Ignorar si la consola está redirigida
            }

            WriteColoredLine("╔══════════════════════════════════════════════════════════════════════╗", ConsoleColor.Cyan);
            WriteColoredLine("║          FRACTAL TIC-TAC-TOE  •  MODO MUERTE SÚBITA (3 EN RAYA)     ║", ConsoleColor.Cyan);
            WriteColoredLine("╚══════════════════════════════════════════════════════════════════════╝", ConsoleColor.Cyan);

            Console.WriteLine($"  Modo: {GetModeDescription(mode, bot)}");
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

            Console.Write($"   |   Movs: {state.MoveCount}/81   |   Zobrist: 0x{state.ZobristKey:X16}   |   Último: ");
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
                "Leyenda y Comandos:",
                " • Números grises (0-8): casilla disponible",
                " • Borde/Título VERDE: tablero activo",
                " • 'h' = sugerencia IA | 'm' = cambiar modo/dificultad",
                " • 'r' = reiniciar     | 'q' = salir"
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
                Console.Write("Ingresa 'Tablero Celda' (0-8 0-8) o 'h' para pista IA: ");
            }
            else
            {
                Console.Write($"Tablero #{state.ActiveBoardIndex} activo -> Ingresa 'Celda' (0-8) o 'h' para pista IA: ");
            }
        }

        private static void RenderGameOverBanner(GameState state)
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
