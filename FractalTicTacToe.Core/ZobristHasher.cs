using System;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Generador y calculador determinista de hashes Zobrist de 64 bits para estados de Fractal Tic-Tac-Toe.
    /// Utiliza SplitMix64 para garantizar entropía uniforme en los 64 bits sin colisiones triviales.
    /// </summary>
    public static class ZobristHasher
    {
        // [flatCellIndex (0..80), player (1..2)]
        private static readonly ulong[,] PieceKeys = new ulong[GameState.TotalCells, 3];

        // [activeBoardIndex + 1 (0..9, donde 0 representa FreeMove = -1 y 1..9 representan tableros 0..8)]
        private static readonly ulong[] ActiveBoardKeys = new ulong[GameState.BoardCount + 1];

        /// <summary>
        /// Clave XOR aplicada cuando el turno activo pertenece a Player.O.
        /// </summary>
        public static readonly ulong TurnKey;

        static ZobristHasher()
        {
            ulong seed = 0x9E3779B97F4A7C15UL;

            for (int cell = 0; cell < GameState.TotalCells; cell++)
            {
                PieceKeys[cell, (int)Player.X] = NextSplitMix64(ref seed);
                PieceKeys[cell, (int)Player.O] = NextSplitMix64(ref seed);
            }

            for (int boardIdx = 0; boardIdx <= GameState.BoardCount; boardIdx++)
            {
                ActiveBoardKeys[boardIdx] = NextSplitMix64(ref seed);
            }

            TurnKey = NextSplitMix64(ref seed);
        }

        public static ulong GetPieceKey(int flatCellIndex, Player player)
        {
            return PieceKeys[flatCellIndex, (int)player];
        }

        public static ulong GetActiveBoardKey(int activeBoardIndex)
        {
            return ActiveBoardKeys[activeBoardIndex + 1];
        }

        /// <summary>
        /// Recalcula desde cero el hash Zobrist completo de un <see cref="GameState"/>.
        /// Útil para inicializar o auditar la actualización incremental en pruebas unitarias.
        /// </summary>
        public static ulong ComputeFullHash(GameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            ulong hash = GetActiveBoardKey(state.ActiveBoardIndex);

            if (state.CurrentPlayer == Player.O)
            {
                hash ^= TurnKey;
            }

            for (int board = 0; board < GameState.BoardCount; board++)
            {
                int baseOffset = board * GameState.CellsPerBoard;
                for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                {
                    Player occupant = state.GetCell(board, cell);
                    if (occupant != Player.None)
                    {
                        hash ^= PieceKeys[baseOffset + cell, (int)occupant];
                    }
                }
            }

            return hash;
        }

        private static ulong NextSplitMix64(ref ulong state)
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
