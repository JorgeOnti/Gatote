namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Niveles de dificultad preconfigurados para el motor Minimax.
    /// Controlan la profundidad máxima, el presupuesto de tiempo por jugada y la activación de tabla de transposición.
    /// </summary>
    public enum BotDifficulty
    {
        /// <summary>
        /// Nivel Principiante: horizonte corto (Profundidad 2, 100 ms) y ligera sub-optimalidad en apertura.
        /// </summary>
        Easy = 1,

        /// <summary>
        /// Nivel Intermedio: detecta amenazas directas y bloqueos tácticos (Profundidad 4, 350 ms).
        /// </summary>
        Medium = 2,

        /// <summary>
        /// Nivel Difícil: búsqueda profunda con Zobrist Transposition Table e Iterative Deepening (Profundidad 6, 1000 ms).
        /// </summary>
        Hard = 3,

        /// <summary>
        /// Nivel Maestro: horizonte táctico extendido (Profundidad 9, 2500 ms) capaz de forzar Zugzwang.
        /// </summary>
        Master = 4,

        /// <summary>
        /// Configuración personalizada definida manualmente por profundidad y límite de tiempo.
        /// </summary>
        Custom = 5
    }
}
