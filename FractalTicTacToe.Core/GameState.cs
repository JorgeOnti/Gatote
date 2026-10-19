using System;
using System.Collections.Generic;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Gestiona el estado completo y las reglas del juego Fractal Tic-Tac-Toe (Muerte Súbita).
    /// Diseñado sin dependencias externas y optimizado para búsqueda adversarial (Minimax).
    /// </summary>
    public sealed class GameState
    {
        public const int BoardCount = 9;
        public const int CellsPerBoard = 9;
        public const int TotalCells = BoardCount * CellsPerBoard; // 81
        public const int FreeMoveBoardIndex = -1;

        /// <summary>
        /// Patrones ganadores de 3 en raya dentro de cualquier tablero pequeño (índices 0..8).
        /// Aplanado en pares de 3 para iteración rápida sin saltos de puntero.
        /// </summary>
        private static readonly int[] WinPatterns =
        {
            0, 1, 2, // Fila superior
            3, 4, 5, // Fila central
            6, 7, 8, // Fila inferior
            0, 3, 6, // Columna izquierda
            1, 4, 7, // Columna central
            2, 5, 8, // Columna derecha
            0, 4, 8, // Diagonal principal
            2, 4, 6  // Diagonal secundaria
        };

        // Arreglo plano de 81 celdas: índice = boardIndex * 9 + cellIndex
        private readonly Player[] _cells;

        // Contador de fichas por cada sub-tablero (0..9) para verificar saturación en O(1)
        private readonly byte[] _boardOccupancy;

        // Total de movimientos realizados en la partida (0..81)
        private int _totalMoves;

        public Player CurrentPlayer { get; private set; }
        public int ActiveBoardIndex { get; private set; }
        public bool IsGameOver { get; private set; }
        public Player Winner { get; private set; }
        public int MoveCount => _totalMoves;

        public GameState()
        {
            _cells = new Player[TotalCells];
            _boardOccupancy = new byte[BoardCount];
            _totalMoves = 0;
            CurrentPlayer = Player.X;
            ActiveBoardIndex = FreeMoveBoardIndex;
            IsGameOver = false;
            Winner = Player.None;
        }

        private GameState(GameState source)
        {
            _cells = new Player[TotalCells];
            Array.Copy(source._cells, _cells, TotalCells);

            _boardOccupancy = new byte[BoardCount];
            Array.Copy(source._boardOccupancy, _boardOccupancy, BoardCount);

            _totalMoves = source._totalMoves;
            CurrentPlayer = source.CurrentPlayer;
            ActiveBoardIndex = source.ActiveBoardIndex;
            IsGameOver = source.IsGameOver;
            Winner = source.Winner;
        }

        /// <summary>
        /// Crea una copia profunda e independiente del estado actual en O(1) para exploración con Minimax.
        /// </summary>
        public GameState Clone() => new GameState(this);

        /// <summary>
        /// Obtiene el contenido de una casilla específica.
        /// </summary>
        public Player GetCell(int boardIndex, int cellIndex)
        {
            if ((uint)boardIndex >= BoardCount)
                throw new ArgumentOutOfRangeException(nameof(boardIndex), "El índice del tablero debe estar entre 0 y 8.");
            if ((uint)cellIndex >= CellsPerBoard)
                throw new ArgumentOutOfRangeException(nameof(cellIndex), "El índice de la celda debe estar entre 0 y 8.");

            return _cells[(boardIndex * CellsPerBoard) + cellIndex];
        }

        /// <summary>
        /// Indexador de solo lectura para consultar una casilla por (boardIndex, cellIndex).
        /// </summary>
        public Player this[int boardIndex, int cellIndex] => GetCell(boardIndex, cellIndex);

        /// <summary>
        /// Genera la lista de todos los movimientos legales disponibles en el turno actual.
        /// Altamente optimizado para Minimax (pre-asigna capacidad exacta para evitar realocaciones).
        /// </summary>
        public List<Move> GetLegalMoves()
        {
            if (IsGameOver)
            {
                return new List<Move>(0);
            }

            // Caso 1: Turno forzado en un único tablero macro (máximo 9 movimientos)
            if (ActiveBoardIndex != FreeMoveBoardIndex)
            {
                int remainingInBoard = CellsPerBoard - _boardOccupancy[ActiveBoardIndex];
                var forcedMoves = new List<Move>(remainingInBoard);
                int baseOffset = ActiveBoardIndex * CellsPerBoard;

                for (int cell = 0; cell < CellsPerBoard; cell++)
                {
                    if (_cells[baseOffset + cell] == Player.None)
                    {
                        forcedMoves.Add(new Move(ActiveBoardIndex, cell));
                    }
                }

                return forcedMoves;
            }

            // Caso 2: Tiro libre en cualquier casilla vacía de cualquier tablero no saturado
            int remainingTotal = TotalCells - _totalMoves;
            var freeMoves = new List<Move>(remainingTotal);

            for (int board = 0; board < BoardCount; board++)
            {
                if (IsBoardFull(board))
                {
                    continue;
                }

                int baseOffset = board * CellsPerBoard;
                for (int cell = 0; cell < CellsPerBoard; cell++)
                {
                    if (_cells[baseOffset + cell] == Player.None)
                    {
                        freeMoves.Add(new Move(board, cell));
                    }
                }
            }

            return freeMoves;
        }

        /// <summary>
        /// Verifica si un movimiento es legal en el estado actual sin mutar el tablero.
        /// </summary>
        public bool IsValidMove(Move move)
        {
            if (IsGameOver)
            {
                return false;
            }

            if ((uint)move.BoardIndex >= BoardCount || (uint)move.CellIndex >= CellsPerBoard)
            {
                return false;
            }

            if (ActiveBoardIndex != FreeMoveBoardIndex && move.BoardIndex != ActiveBoardIndex)
            {
                return false;
            }

            int flatIndex = (move.BoardIndex * CellsPerBoard) + move.CellIndex;
            return _cells[flatIndex] == Player.None;
        }

        /// <summary>
        /// Valida y aplica un movimiento al estado actual.
        /// Actualiza condición de victoria por muerte súbita, empate global, redirección de tablero y turno.
        /// </summary>
        /// <param name="move">Movimiento a ejecutar.</param>
        /// <returns>true si el movimiento fue legal y se aplicó; false en caso contrario.</returns>
        public bool MakeMove(Move move)
        {
            if (!IsValidMove(move))
            {
                return false;
            }

            int flatIndex = (move.BoardIndex * CellsPerBoard) + move.CellIndex;
            _cells[flatIndex] = CurrentPlayer;
            _boardOccupancy[move.BoardIndex]++;
            _totalMoves++;

            // 1. Verificar condición de victoria por Muerte Súbita en el tablero donde se jugó
            if (CheckBoardWin(move.BoardIndex, CurrentPlayer))
            {
                Winner = CurrentPlayer;
                IsGameOver = true;
                ActiveBoardIndex = FreeMoveBoardIndex;
                return true;
            }

            // 2. Verificar condición de empate global (81 casillas ocupadas sin ganador)
            if (_totalMoves >= TotalCells)
            {
                Winner = Player.None;
                IsGameOver = true;
                ActiveBoardIndex = FreeMoveBoardIndex;
                return true;
            }

            // 3. Redirección recíproca: la celda elegida define el siguiente tablero macro
            // Si el tablero destino está saturado (9 casillas llenas), se otorga Tiro Libre (-1)
            int targetBoard = move.CellIndex;
            ActiveBoardIndex = IsBoardFull(targetBoard) ? FreeMoveBoardIndex : targetBoard;

            // 4. Alternar jugador activo
            CurrentPlayer = CurrentPlayer == Player.X ? Player.O : Player.X;

            return true;
        }

        /// <summary>
        /// Determina si un sub-tablero específico tiene sus 9 casillas ocupadas.
        /// </summary>
        private bool IsBoardFull(int boardIndex)
        {
            return _boardOccupancy[boardIndex] >= CellsPerBoard;
        }

        /// <summary>
        /// Verifica si el jugador especificado acaba de formar una línea de 3 en raya
        /// dentro del tablero pequeño indicado.
        /// </summary>
        private bool CheckBoardWin(int boardIndex, Player player)
        {
            // Un jugador no puede tener 3 en raya en este sub-tablero si hay menos de 3 fichas en él
            if (_boardOccupancy[boardIndex] < 3)
            {
                return false;
            }

            int baseOffset = boardIndex * CellsPerBoard;

            for (int i = 0; i < WinPatterns.Length; i += 3)
            {
                if (_cells[baseOffset + WinPatterns[i]] == player &&
                    _cells[baseOffset + WinPatterns[i + 1]] == player &&
                    _cells[baseOffset + WinPatterns[i + 2]] == player)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
