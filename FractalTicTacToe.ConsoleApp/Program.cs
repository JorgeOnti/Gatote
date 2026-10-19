using System;
using System.Collections.Generic;
using System.Text;
using FractalTicTacToe.Core;

namespace FractalTicTacToe.ConsoleApp
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var state = new GameState();
            Move? lastMove = null;
            string statusMessage = null;
            bool isErrorMessage = false;

            while (true)
            {
                RenderScreen(state, lastMove, statusMessage, isErrorMessage);
                statusMessage = null;
                isErrorMessage = false;

                if (state.IsGameOver)
                {
                    RenderGameOverBanner(state);
                    Console.Write("\n¿Desean jugar otra partida? (s/n): ");
                    string retry = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (retry == "s" || retry == "si" || retry == "sí" || retry == "y")
                    {
                        state = new GameState();
                        lastMove = null;
                        continue;
                    }

                    Console.WriteLine("¡Gracias por jugar Fractal Tic-Tac-Toe!");
                    break;
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
                    lastMove = null;
                    statusMessage = "Partida reiniciada.";
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

            // Si el tablero está forzado (0..8), permitimos ingresar solo la celda (1 número) o tablero y celda (2 números)
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

        private static void RenderScreen(GameState state, Move? lastMove, string statusMessage, bool isErrorMessage)
        {
            try
            {
                Console.Clear();
            }
            catch
            {
                // Ignorar si la consola está redirigida en pruebas automatizadas
            }

            WriteColoredLine("╔══════════════════════════════════════════════════════════════════════╗", ConsoleColor.Cyan);
            WriteColoredLine("║          FRACTAL TIC-TAC-TOE  •  MODO MUERTE SÚBITA (3 EN RAYA)     ║", ConsoleColor.Cyan);
            WriteColoredLine("╚══════════════════════════════════════════════════════════════════════╝", ConsoleColor.Cyan);

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

            Console.Write($"   |   Movimientos: {state.MoveCount}/81   |   Último tiro: ");
            if (lastMove.HasValue)
            {
                WriteColoredLine($"Tablero {lastMove.Value.BoardIndex}, Celda {lastMove.Value.CellIndex}", ConsoleColor.White);
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
                "Leyenda:",
                " • Números grises (0-8): casilla vacía disponible",
                " • Borde/Título VERDE: tablero activo donde debes tirar",
                " • [X] / [O]: último movimiento realizado",
                " • Comandos: 'r' = reiniciar, 'q' = salir"
            };

            int guideLineIndex = 0;

            for (int macroRow = 0; macroRow < 3; macroRow++)
            {
                // Encabezados de los 3 tableros macro de esta fila
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

                // Las 3 filas internas de celdas dentro de esta banda de tableros macro
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

                // Separador horizontal entre filas de tableros macro
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
                // Casilla vacía: mostramos su índice en gris si el tablero es jugable, o un punto si está bloqueado
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
                Console.Write("Ingresa 'Tablero Celda' (0-8 0-8): ");
            }
            else
            {
                Console.Write($"Tablero #{state.ActiveBoardIndex} activo -> Ingresa 'Celda' (0-8) o '{state.ActiveBoardIndex} Celda': ");
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
