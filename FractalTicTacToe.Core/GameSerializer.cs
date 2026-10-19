using System;
using System.Collections.Generic;
using System.Text;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Serializador sin dependencias externas para exportar e importar partidas de Fractal Tic-Tac-Toe
    /// en notación compacta FEN (instantánea de estado), transcripción de movimientos (historial completo)
    /// y formato PGN extendido (<see cref="MatchRecord"/>).
    /// </summary>
    public static class GameSerializer
    {
        /// <summary>
        /// Exporta el estado actual a una cadena compacta estilo FEN (Forsyth-Edwards Notation).
        /// Ejemplo inicial: <c>"9/9/9/9/9/9/9/9/9 X -1 *"</c>
        /// </summary>
        public static string ToFen(GameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var sb = new StringBuilder(64);

            for (int board = 0; board < GameState.BoardCount; board++)
            {
                int emptyRun = 0;
                for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                {
                    Player p = state.GetCell(board, cell);
                    if (p == Player.None)
                    {
                        emptyRun++;
                    }
                    else
                    {
                        if (emptyRun > 0)
                        {
                            sb.Append(emptyRun);
                            emptyRun = 0;
                        }
                        sb.Append(p == Player.X ? 'X' : 'O');
                    }
                }

                if (emptyRun > 0)
                {
                    sb.Append(emptyRun);
                }

                if (board < GameState.BoardCount - 1)
                {
                    sb.Append('/');
                }
            }

            char turnChar = state.CurrentPlayer == Player.O ? 'O' : 'X';
            char statusChar = '*'; // En curso
            if (state.IsGameOver)
            {
                if (state.Winner == Player.X)
                {
                    statusChar = 'X';
                }
                else if (state.Winner == Player.O)
                {
                    statusChar = 'O';
                }
                else
                {
                    statusChar = 'D'; // Empate global (Draw)
                }
            }

            sb.Append(' ')
              .Append(turnChar)
              .Append(' ')
              .Append(state.ActiveBoardIndex)
              .Append(' ')
              .Append(statusChar);

            return sb.ToString();
        }

        /// <summary>
        /// Reconstruye un <see cref="GameState"/> a partir de una cadena en formato FEN.
        /// </summary>
        public static GameState FromFen(string fen)
        {
            if (string.IsNullOrWhiteSpace(fen))
            {
                throw new ArgumentException("La cadena FEN no puede estar vacía.", nameof(fen));
            }

            string[] sections = fen.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (sections.Length < 3)
            {
                throw new FormatException("Formato FEN inválido: se requieren al menos los tableros, el turno y el tablero activo.");
            }

            string[] boardSegments = sections[0].Split('/');
            if (boardSegments.Length != GameState.BoardCount)
            {
                throw new FormatException($"Formato FEN inválido: se esperaban 9 sub-tableros separados por '/', pero se encontraron {boardSegments.Length}.");
            }

            var cells = new Player[GameState.TotalCells];

            for (int board = 0; board < GameState.BoardCount; board++)
            {
                string segment = boardSegments[board];
                int cellIndex = 0;
                int baseOffset = board * GameState.CellsPerBoard;

                for (int i = 0; i < segment.Length; i++)
                {
                    char c = segment[i];
                    if (c >= '1' && c <= '9')
                    {
                        int emptyCount = c - '0';
                        cellIndex += emptyCount;
                    }
                    else if (c == 'X' || c == 'x')
                    {
                        if (cellIndex >= GameState.CellsPerBoard)
                        {
                            throw new FormatException($"El sub-tablero {board} en el FEN excede las 9 celdas.");
                        }
                        cells[baseOffset + cellIndex] = Player.X;
                        cellIndex++;
                    }
                    else if (c == 'O' || c == 'o')
                    {
                        if (cellIndex >= GameState.CellsPerBoard)
                        {
                            throw new FormatException($"El sub-tablero {board} en el FEN excede las 9 celdas.");
                        }
                        cells[baseOffset + cellIndex] = Player.O;
                        cellIndex++;
                    }
                    else
                    {
                        throw new FormatException($"Carácter inválido '{c}' en el sub-tablero {board} del FEN.");
                    }
                }

                if (cellIndex != GameState.CellsPerBoard)
                {
                    throw new FormatException($"El sub-tablero {board} en el FEN define {cellIndex} celdas en lugar de 9.");
                }
            }

            string turnToken = sections[1].ToUpperInvariant();
            if (turnToken != "X" && turnToken != "O")
            {
                throw new FormatException($"Turno inválido '{sections[1]}' en FEN. Use 'X' u 'O'.");
            }
            Player currentPlayer = turnToken == "O" ? Player.O : Player.X;

            if (!int.TryParse(sections[2], out int activeBoard) || activeBoard < -1 || activeBoard > 8)
            {
                throw new FormatException($"ActiveBoardIndex inválido '{sections[2]}' en FEN. Debe estar entre -1 y 8.");
            }

            bool isGameOver = false;
            Player winner = Player.None;

            if (sections.Length >= 4)
            {
                string statusToken = sections[3].ToUpperInvariant();
                if (statusToken == "X")
                {
                    isGameOver = true;
                    winner = Player.X;
                }
                else if (statusToken == "O")
                {
                    isGameOver = true;
                    winner = Player.O;
                }
                else if (statusToken == "D" || statusToken == "DRAW")
                {
                    isGameOver = true;
                    winner = Player.None;
                }
            }

            var state = new GameState();
            state.LoadFromSnapshot(cells, currentPlayer, activeBoard, isGameOver, winner);
            return state;
        }

        /// <summary>
        /// Exporta la lista cronológica de movimientos realizados en la partida como una cadena compacta.
        /// Cada tiro se codifica como dos dígitos <c>BC</c> (Board, Cell) separados por guiones (ej. <c>"01-10-02-20-00"</c>).
        /// </summary>
        public static string ExportMoveHistory(GameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            List<Move> moves = state.GetMoveHistory();
            if (moves.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(moves.Count * 3);
            for (int i = 0; i < moves.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('-');
                }
                sb.Append(moves[i].BoardIndex).Append(moves[i].CellIndex);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reconstruye un <see cref="GameState"/> completo (con soporte íntegro de <see cref="GameState.UndoMove"/>)
        /// reproduciendo paso a paso una transcripción de movimientos (ej. <c>"01-10-02-20-00"</c>).
        /// </summary>
        public static GameState ImportMoveHistory(string moveTranscript)
        {
            var state = new GameState();
            if (string.IsNullOrWhiteSpace(moveTranscript))
            {
                return state;
            }

            string[] tokens = moveTranscript.Trim().Split(
                new[] { '-', ' ', ',', ';', '\r', '\n', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (token.Length != 2 ||
                    token[0] < '0' || token[0] > '8' ||
                    token[1] < '0' || token[1] > '8')
                {
                    throw new FormatException(
                        $"Token de movimiento inválido '{token}' en el paso #{i + 1}. Se esperaba un par de dígitos 'BC' entre 00 y 88.");
                }

                var move = new Move(token[0] - '0', token[1] - '0');
                if (!state.MakeMove(move))
                {
                    throw new InvalidOperationException(
                        $"El movimiento #{i + 1} ({move}) es ilegal según las reglas del juego en ese punto de la partida.");
                }
            }

            return state;
        }
    }
}
