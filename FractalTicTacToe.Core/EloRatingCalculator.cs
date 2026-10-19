using System;

namespace FractalTicTacToe.Core
{
#if NET6_0_OR_GREATER
    /// <summary>
    /// Resultado del cálculo de actualización de puntos ELO tras concluir una partida competitiva.
    /// </summary>
    public readonly record struct EloMatchUpdate(
        int NewRatingX,
        int DeltaX,
        int NewRatingO,
        int DeltaO);
#else
    /// <summary>
    /// Resultado del cálculo de actualización de puntos ELO tras concluir una partida competitiva.
    /// </summary>
    public readonly struct EloMatchUpdate : IEquatable<EloMatchUpdate>
    {
        public int NewRatingX { get; }
        public int DeltaX { get; }
        public int NewRatingO { get; }
        public int DeltaO { get; }

        public EloMatchUpdate(int newRatingX, int deltaX, int newRatingO, int deltaO)
        {
            NewRatingX = newRatingX;
            DeltaX = deltaX;
            NewRatingO = newRatingO;
            DeltaO = deltaO;
        }

        public bool Equals(EloMatchUpdate other) =>
            NewRatingX == other.NewRatingX &&
            DeltaX == other.DeltaX &&
            NewRatingO == other.NewRatingO &&
            DeltaO == other.DeltaO;

        public override bool Equals(object obj) => obj is EloMatchUpdate other && Equals(other);

        public override int GetHashCode() =>
            (((NewRatingX * 397) ^ DeltaX) * 397 ^ NewRatingO) * 397 ^ DeltaO;
    }
#endif

    /// <summary>
    /// Calculadora de puntuación competitiva tipo Ajedrez (Sistema ELO de la FIDE con factor K dinámico).
    /// Diseñada en la capa de dominio para ser compartida por el servidor multijugador y el cliente.
    /// </summary>
    public static class EloRatingCalculator
    {
        public const int DefaultInitialRating = 1200;
        public const int MinimumRatingFloor = 100;

        /// <summary>
        /// Calcula la probabilidad esperada de victoria (0.0 a 1.0) del jugador A frente al jugador B.
        /// Fórmula estándar: <c>E_A = 1 / (1 + 10^((R_B - R_A) / 400))</c>
        /// </summary>
        public static double CalculateExpectedScore(int ratingA, int ratingB)
        {
            double exponent = (ratingB - ratingA) / 400.0;
            return 1.0 / (1.0 + Math.Pow(10.0, exponent));
        }

        /// <summary>
        /// Determina el factor de desarrollo K estilo FIDE:
        /// - K = 40 para jugadores provisionales (menos de 10 partidas jugadas).
        /// - K = 24 para jugadores estándar con rating menor a 2000.
        /// - K = 16 para jugadores de nivel Maestro (2000+ ELO).
        /// </summary>
        public static int GetKFactor(int currentRating, int gamesPlayed)
        {
            if (gamesPlayed < 10)
            {
                return 40;
            }

            if (currentRating < 2000)
            {
                return 24;
            }

            return 16;
        }

        /// <summary>
        /// Calcula los nuevos ratings y deltas de puntos ELO para ambos jugadores tras finalizar una partida.
        /// </summary>
        /// <param name="ratingX">ELO actual del Jugador X.</param>
        /// <param name="gamesPlayedX">Partidas previas jugadas por X.</param>
        /// <param name="ratingO">ELO actual del Jugador O.</param>
        /// <param name="gamesPlayedO">Partidas previas jugadas por O.</param>
        /// <param name="winner">Ganador de la partida (<see cref="Player.X"/>, <see cref="Player.O"/> o <see cref="Player.None"/> para empate).</param>
        public static EloMatchUpdate CalculateMatchRatingChange(
            int ratingX,
            int gamesPlayedX,
            int ratingO,
            int gamesPlayedO,
            Player winner)
        {
            double expectedX = CalculateExpectedScore(ratingX, ratingO);
            double expectedO = 1.0 - expectedX;

            double actualScoreX;
            double actualScoreO;

            if (winner == Player.X)
            {
                actualScoreX = 1.0;
                actualScoreO = 0.0;
            }
            else if (winner == Player.O)
            {
                actualScoreX = 0.0;
                actualScoreO = 1.0;
            }
            else
            {
                actualScoreX = 0.5;
                actualScoreO = 0.5;
            }

            int kX = GetKFactor(ratingX, gamesPlayedX);
            int kO = GetKFactor(ratingO, gamesPlayedO);

            int deltaX = (int)Math.Round(kX * (actualScoreX - expectedX), MidpointRounding.AwayFromZero);
            int deltaO = (int)Math.Round(kO * (actualScoreO - expectedO), MidpointRounding.AwayFromZero);

            int newRatingX = Math.Max(MinimumRatingFloor, ratingX + deltaX);
            int newRatingO = Math.Max(MinimumRatingFloor, ratingO + deltaO);

            return new EloMatchUpdate(
                newRatingX,
                newRatingX - ratingX,
                newRatingO,
                newRatingO - ratingO);
        }

        /// <summary>
        /// Obtiene el rango competitivo correspondiente al puntaje ELO del jugador.
        /// </summary>
        public static string GetRankTitle(int rating)
        {
            if (rating < 1000) return "Bronce";
            if (rating < 1200) return "Plata";
            if (rating < 1400) return "Oro";
            if (rating < 1600) return "Platino";
            if (rating < 1900) return "Diamante";
            if (rating < 2200) return "Maestro Fractal";
            return "Gran Maestro Fractal";
        }
    }
}
