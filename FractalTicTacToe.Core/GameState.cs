using System;
using System.Collections.Generic;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Gestiona el estado completo, el historial de movimientos (Undo/Redo) y las reglas
    /// del juego Fractal Tic-Tac-Toe (Muerte Súbita).
    /// Diseñado sin dependencias externas y optimizado para Minimax y sincronización en red.
    /// </summary>
    public sealed class GameState
    {
        public const int BoardCount = 9;
        public const int CellsPerBoard = 9;
        public const int TotalCells = BoardCount * CellsPerBoard; // 81
        public const int FreeMoveBoardIndex = -1;

        /// <summary>
        /// Instantánea ligera de transición previa para deshacer un movimiento en O(1) exacto.
        /// </summary>
        private struct MoveUndoRecord
        {
            public Move Move;
            public Player PlayerWhoMoved;
            public int PreviousActiveBoardIndex;
            public ulong PreviousZobristKey;
        }

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

        // Pilas pre-asignadas de tamaño fijo (81) para Undo/Redo sin asignaciones en el heap
        private readonly MoveUndoRecord[] _undoStack;
        private readonly Move[] _redoStack;
        private int _undoCount;
        private int _redoCount;

        // Total de movimientos realizados en el tablero actual (0..81)
        private int _totalMoves;

        public Player CurrentPlayer { get; private set; }
        public int ActiveBoardIndex { get; private set; }
        public bool IsGameOver { get; private set; }
        public Player Winner { get; private set; }
        public int MoveCount => _totalMoves;

        public bool CanUndo => _undoCount > 0;
        public bool CanRedo => _redoCount > 0;
        public int UndoCount => _undoCount;
        public int RedoCount => _redoCount;

        /// <summary>
        /// Hash Zobrist incremental de 64 bits que identifica unívocamente la posición,
        /// el jugador en turno y el tablero macro activo.
        /// </summary>
        public ulong ZobristKey { get; private set; }

        public GameState()
        {
            _cells = new Player[TotalCells];
            _boardOccupancy = new byte[BoardCount];
            _undoStack = new MoveUndoRecord[TotalCells];
            _redoStack = new Move[TotalCells];
            Reset();
        }

        private GameState(GameState source)
        {
            _cells = new Player[TotalCells];
            Array.Copy(source._cells, _cells, TotalCells);

            _boardOccupancy = new byte[BoardCount];
            Array.Copy(source._boardOccupancy, _boardOccupancy, BoardCount);

            _undoStack = new MoveUndoRecord[TotalCells];
            if (source._undoCount > 0)
            {
                Array.Copy(source._undoStack, _undoStack, source._undoCount);
            }

            _redoStack = new Move[TotalCells];
            if (source._redoCount > 0)
            {
                Array.Copy(source._redoStack, _redoStack, source._redoCount);
            }

            _undoCount = source._undoCount;
            _redoCount = source._redoCount;
            _totalMoves = source._totalMoves;
            CurrentPlayer = source.CurrentPlayer;
            ActiveBoardIndex = source.ActiveBoardIndex;
            IsGameOver = source.IsGameOver;
            Winner = source.Winner;
            ZobristKey = source.ZobristKey;
        }

        /// <summary>
        /// Reinicia el tablero y el historial a su estado inicial limpio.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_cells, 0, TotalCells);
            Array.Clear(_boardOccupancy, 0, BoardCount);
            _undoCount = 0;
            _redoCount = 0;
            _totalMoves = 0;
            CurrentPlayer = Player.X;
            ActiveBoardIndex = FreeMoveBoardIndex;
            IsGameOver = false;
            Winner = Player.None;
            ZobristKey = ZobristHasher.GetActiveBoardKey(FreeMoveBoardIndex);
        }

        /// <summary>
        /// Crea una copia profunda e independiente del estado actual.
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
        /// Devuelve cuántas casillas vacías quedan en un sub-tablero (0..9) en O(1).
        /// </summary>
        public int GetBoardEmptyCount(int boardIndex)
        {
            if ((uint)boardIndex >= BoardCount)
                throw new ArgumentOutOfRangeException(nameof(boardIndex));
            return CellsPerBoard - _boardOccupancy[boardIndex];
        }

        /// <summary>
        /// Obtiene el último movimiento aplicado en la partida, o null si no hay movimientos en el historial.
        /// </summary>
        public Move? GetLastMove()
        {
            if (_undoCount == 0)
            {
                return null;
            }

            return _undoStack[_undoCount - 1].Move;
        }

        /// <summary>
        /// Retorna la secuencia cronológica de movimientos aplicados desde el inicio del estado.
        /// </summary>
        public List<Move> GetMoveHistory()
        {
            var history = new List<Move>(_undoCount);
            for (int i = 0; i < _undoCount; i++)
            {
                history.Add(_undoStack[i].Move);
            }
            return history;
        }

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
        /// Valida y aplica un movimiento al estado actual, registrándolo en la pila de Undo y limpiando Redo.
        /// </summary>
        public bool MakeMove(Move move)
        {
            if (!ApplyMoveInternal(move))
            {
                return false;
            }

            // Al realizar un movimiento nuevo, se invalida cualquier línea futura de Redo
            _redoCount = 0;
            return true;
        }

        /// <summary>
        /// Deshace el último movimiento realizado en O(1) y lo almacena en la pila de Redo.
        /// </summary>
        /// <returns>true si había un movimiento por deshacer; false en caso contrario.</returns>
        public bool UndoMove()
        {
            if (_undoCount == 0)
            {
                return false;
            }

            MoveUndoRecord record = PopUndoInternal();
            _redoStack[_redoCount++] = record.Move;
            return true;
        }

        /// <summary>
        /// Rehace el último movimiento deshecho previamente con <see cref="UndoMove"/>.
        /// </summary>
        /// <returns>true si había un movimiento en la pila de Redo; false en caso contrario.</returns>
        public bool RedoMove()
        {
            if (_redoCount == 0)
            {
                return false;
            }

            Move moveToRedo = _redoStack[--_redoCount];
            return ApplyMoveInternal(moveToRedo);
        }

        /// <summary>
        /// Declara el fin inmediato de la partida por agotamiento de tiempo en el reloj del jugador indicado.
        /// </summary>
        public bool ForfeitByTimeout(Player timedOutPlayer)
        {
            if (IsGameOver || timedOutPlayer == Player.None)
            {
                return false;
            }

            ForceConclude(timedOutPlayer == Player.X ? Player.O : Player.X);
            return true;
        }

        /// <summary>
        /// Fuerza la conclusión inmediata del estado (por rendición, tiempo agotado, desconexión o cancelación),
        /// garantizando que <see cref="IsGameOver"/>, <see cref="Winner"/> y la exportación FEN reflejen el resultado.
        /// </summary>
        public void ForceConclude(Player winner)
        {
            if (!IsGameOver)
            {
                ZobristKey ^= ZobristHasher.GetActiveBoardKey(ActiveBoardIndex);
                ActiveBoardIndex = FreeMoveBoardIndex;
                ZobristKey ^= ZobristHasher.GetActiveBoardKey(FreeMoveBoardIndex);
            }

            Winner = winner;
            IsGameOver = true;
        }

        /// <summary>
        /// Si la partida terminó por 3 en raya (Muerte Súbita), obtiene el sub-tablero y los 3 índices de celda ganadores
        /// para resaltarlos y animarlos en la interfaz gráfica.
        /// </summary>
        public bool TryGetWinningLine(out int boardIndex, out int cell0, out int cell1, out int cell2)
        {
            boardIndex = -1;
            cell0 = -1;
            cell1 = -1;
            cell2 = -1;

            if (!IsGameOver || Winner == Player.None)
            {
                return false;
            }

            for (int b = 0; b < BoardCount; b++)
            {
                if (_boardOccupancy[b] < 3)
                {
                    continue;
                }

                int baseOffset = b * CellsPerBoard;
                for (int i = 0; i < WinPatterns.Length; i += 3)
                {
                    int c0 = WinPatterns[i];
                    int c1 = WinPatterns[i + 1];
                    int c2 = WinPatterns[i + 2];

                    if (_cells[baseOffset + c0] == Winner &&
                        _cells[baseOffset + c1] == Winner &&
                        _cells[baseOffset + c2] == Winner)
                    {
                        boardIndex = b;
                        cell0 = c0;
                        cell1 = c1;
                        cell2 = c2;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Método interno de ultra-baja latencia usado por <see cref="MinimaxBot"/> para revertir un nodo
        /// durante la búsqueda en el árbol sin alterar la pila de Redo.
        /// </summary>
        internal void UnmakeMoveForSearch()
        {
            PopUndoInternal();
        }

        /// <summary>
        /// Reconstruye un estado directamente desde una instantánea externa (p. ej. cadena FEN o editor de posiciones).
        /// </summary>
        public void LoadFromSnapshot(
            Player[] cells,
            Player currentPlayer,
            int activeBoardIndex,
            bool isGameOver,
            Player winner)
        {
            Reset();

            for (int board = 0; board < BoardCount; board++)
            {
                int baseOffset = board * CellsPerBoard;
                for (int cell = 0; cell < CellsPerBoard; cell++)
                {
                    Player p = cells[baseOffset + cell];
                    _cells[baseOffset + cell] = p;
                    if (p != Player.None)
                    {
                        _boardOccupancy[board]++;
                        _totalMoves++;
                    }
                }
            }

            CurrentPlayer = currentPlayer;
            ActiveBoardIndex = isGameOver ? FreeMoveBoardIndex : activeBoardIndex;
            IsGameOver = isGameOver;
            Winner = winner;
            ZobristKey = ZobristHasher.ComputeFullHash(this);
        }

        private bool ApplyMoveInternal(Move move)
        {
            if (!IsValidMove(move))
            {
                return false;
            }

            // Guardar instantánea de transición en la pila de Undo antes de mutar
            _undoStack[_undoCount++] = new MoveUndoRecord
            {
                Move = move,
                PlayerWhoMoved = CurrentPlayer,
                PreviousActiveBoardIndex = ActiveBoardIndex,
                PreviousZobristKey = ZobristKey
            };

            int flatIndex = (move.BoardIndex * CellsPerBoard) + move.CellIndex;

            ZobristKey ^= ZobristHasher.GetActiveBoardKey(ActiveBoardIndex);
            ZobristKey ^= ZobristHasher.GetPieceKey(flatIndex, CurrentPlayer);

            _cells[flatIndex] = CurrentPlayer;
            _boardOccupancy[move.BoardIndex]++;
            _totalMoves++;

            // 1. Verificar condición de victoria por Muerte Súbita en el tablero donde se jugó
            if (CheckBoardWin(move.BoardIndex, CurrentPlayer))
            {
                Winner = CurrentPlayer;
                IsGameOver = true;
                ActiveBoardIndex = FreeMoveBoardIndex;
                ZobristKey ^= ZobristHasher.GetActiveBoardKey(FreeMoveBoardIndex);
                return true;
            }

            // 2. Verificar condición de empate global (81 casillas ocupadas sin ganador)
            if (_totalMoves >= TotalCells)
            {
                Winner = Player.None;
                IsGameOver = true;
                ActiveBoardIndex = FreeMoveBoardIndex;
                ZobristKey ^= ZobristHasher.GetActiveBoardKey(FreeMoveBoardIndex);
                return true;
            }

            // 3. Redirección recíproca: la celda elegida define el siguiente tablero macro
            int targetBoard = move.CellIndex;
            ActiveBoardIndex = IsBoardFull(targetBoard) ? FreeMoveBoardIndex : targetBoard;
            ZobristKey ^= ZobristHasher.GetActiveBoardKey(ActiveBoardIndex);

            // 4. Alternar jugador activo y actualizar paridad de turno en el hash Zobrist
            CurrentPlayer = CurrentPlayer == Player.X ? Player.O : Player.X;
            ZobristKey ^= ZobristHasher.TurnKey;

            return true;
        }

        private MoveUndoRecord PopUndoInternal()
        {
            MoveUndoRecord record = _undoStack[--_undoCount];
            int flatIndex = (record.Move.BoardIndex * CellsPerBoard) + record.Move.CellIndex;

            _cells[flatIndex] = Player.None;
            _boardOccupancy[record.Move.BoardIndex]--;
            _totalMoves--;

            CurrentPlayer = record.PlayerWhoMoved;
            ActiveBoardIndex = record.PreviousActiveBoardIndex;
            IsGameOver = false;
            Winner = Player.None;
            ZobristKey = record.PreviousZobristKey;

            return record;
        }

        private bool IsBoardFull(int boardIndex)
        {
            return _boardOccupancy[boardIndex] >= CellsPerBoard;
        }

        private bool CheckBoardWin(int boardIndex, Player player)
        {
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
