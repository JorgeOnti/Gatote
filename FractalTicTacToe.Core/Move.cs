using System;

namespace FractalTicTacToe.Core
{
#if NET6_0_OR_GREATER
    /// <summary>
    /// Representa un movimiento inmutable dentro del tablero fractal.
    /// </summary>
    /// <param name="BoardIndex">Índice del tablero macro (0 a 8).</param>
    /// <param name="CellIndex">Índice de la casilla local dentro del tablero pequeño (0 a 8).</param>
    public readonly record struct Move(int BoardIndex, int CellIndex)
    {
        public override string ToString() => $"(Board: {BoardIndex}, Cell: {CellIndex})";
    }
#else
    /// <summary>
    /// Representa un movimiento inmutable dentro del tablero fractal.
    /// Compatible con .NET Standard 2.0 / .NET Core 3.1 y semánticamente idéntico a readonly record struct.
    /// </summary>
    public readonly struct Move : IEquatable<Move>
    {
        public int BoardIndex { get; }
        public int CellIndex { get; }

        public Move(int boardIndex, int cellIndex)
        {
            BoardIndex = boardIndex;
            CellIndex = cellIndex;
        }

        public void Deconstruct(out int boardIndex, out int cellIndex)
        {
            boardIndex = BoardIndex;
            cellIndex = CellIndex;
        }

        public bool Equals(Move other) => BoardIndex == other.BoardIndex && CellIndex == other.CellIndex;

        public override bool Equals(object obj) => obj is Move other && Equals(other);

        public override int GetHashCode() => (BoardIndex * 397) ^ CellIndex;

        public static bool operator ==(Move left, Move right) => left.Equals(right);

        public static bool operator !=(Move left, Move right) => !left.Equals(right);

        public override string ToString() => $"(Board: {BoardIndex}, Cell: {CellIndex})";
    }
#endif
}
