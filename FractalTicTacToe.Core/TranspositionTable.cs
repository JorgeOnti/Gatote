using System;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Tipo de cota almacenada en una entrada de la tabla de transposición.
    /// </summary>
    public enum TranspositionFlag : byte
    {
        None = 0,
        Exact = 1,      // Valor exacto (alpha < score < beta)
        LowerBound = 2, // Poda Beta (score >= beta)
        UpperBound = 3  // Poda Alfa (score <= alpha)
    }

    /// <summary>
    /// Entrada compacta por valor dentro de la tabla de transposición.
    /// </summary>
    public struct TranspositionEntry
    {
        public ulong Key;
        public int Score;
        public Move BestMove;
        public short Depth;
        public TranspositionFlag Flag;
    }

    /// <summary>
    /// Tabla de transposición indexada mediante máscaras de bits sobre hashes Zobrist de 64 bits.
    /// Diseñada con un arreglo plano pre-asignado de tamaño potencia de 2 para evitar asignaciones en tiempo de búsqueda.
    /// </summary>
    public sealed class TranspositionTable
    {
        public const int DefaultSizePowerOfTwo = 18; // 2^18 = 262,144 entradas (~6 MB)

        private readonly TranspositionEntry[] _entries;
        private readonly ulong _indexMask;

        public int Capacity => _entries.Length;
        public int Hits { get; private set; }
        public int Cutoffs { get; private set; }

        /// <summary>
        /// Inicializa una tabla de transposición con <c>2^sizePowerOfTwo</c> entradas.
        /// </summary>
        /// <param name="sizePowerOfTwo">Exponente de potencia de 2 (entre 10 y 22).</param>
        public TranspositionTable(int sizePowerOfTwo = DefaultSizePowerOfTwo)
        {
            if (sizePowerOfTwo < 10 || sizePowerOfTwo > 22)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sizePowerOfTwo),
                    "El exponente de capacidad debe estar entre 10 (1,024 entradas) y 22 (4,194,304 entradas).");
            }

            int size = 1 << sizePowerOfTwo;
            _entries = new TranspositionEntry[size];
            _indexMask = (ulong)(size - 1);
        }

        public void Clear()
        {
            Array.Clear(_entries, 0, _entries.Length);
            ResetMetrics();
        }

        public void ResetMetrics()
        {
            Hits = 0;
            Cutoffs = 0;
        }

        public void RecordCutoff()
        {
            Cutoffs++;
        }

        /// <summary>
        /// Consulta si el estado con clave <paramref name="zobristKey"/> se encuentra almacenado en la tabla.
        /// </summary>
        public bool TryProbe(ulong zobristKey, out TranspositionEntry entry)
        {
            int index = (int)(zobristKey & _indexMask);
            entry = _entries[index];

            if (entry.Flag != TranspositionFlag.None && entry.Key == zobristKey)
            {
                Hits++;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Almacena una evaluación en la tabla utilizando política de reemplazo preferente por profundidad.
        /// </summary>
        public void Store(
            ulong zobristKey,
            int depth,
            int score,
            TranspositionFlag flag,
            Move bestMove)
        {
            int index = (int)(zobristKey & _indexMask);
            ref TranspositionEntry existing = ref _entries[index];

            // Reemplazamos si la celda está vacía, si es una colisión con menor o igual profundidad,
            // o si es exactamente la misma posición actualizada a mayor o igual profundidad.
            if (existing.Flag == TranspositionFlag.None ||
                existing.Key != zobristKey ||
                depth >= existing.Depth)
            {
                existing.Key = zobristKey;
                existing.Score = score;
                existing.BestMove = bestMove;
                existing.Depth = (short)depth;
                existing.Flag = flag;
            }
        }
    }
}
