using System;

namespace FractalTicTacToe.Core
{
#if NET6_0_OR_GREATER
    /// <summary>
    /// Configuración de control de tiempo estilo ajedrez (tiempo inicial + incremento Fischer por jugada).
    /// </summary>
    public readonly record struct TimeControlConfig(
        string Name,
        double InitialSeconds,
        double IncrementSeconds)
    {
        public static TimeControlConfig Bullet1Plus1 => new TimeControlConfig("Bullet 1+1", 60.0, 1.0);
        public static TimeControlConfig Blitz3Plus0 => new TimeControlConfig("Blitz 3+0", 180.0, 0.0);
        public static TimeControlConfig Blitz3Plus2 => new TimeControlConfig("Blitz 3+2", 180.0, 2.0);
        public static TimeControlConfig Rapid5Plus0 => new TimeControlConfig("Rápida 5+0", 300.0, 0.0);
    }
#else
    /// <summary>
    /// Configuración de control de tiempo estilo ajedrez (tiempo inicial + incremento Fischer por jugada).
    /// </summary>
    public readonly struct TimeControlConfig : IEquatable<TimeControlConfig>
    {
        public string Name { get; }
        public double InitialSeconds { get; }
        public double IncrementSeconds { get; }

        public TimeControlConfig(string name, double initialSeconds, double incrementSeconds)
        {
            Name = name;
            InitialSeconds = initialSeconds;
            IncrementSeconds = incrementSeconds;
        }

        public static TimeControlConfig Bullet1Plus1 => new TimeControlConfig("Bullet 1+1", 60.0, 1.0);
        public static TimeControlConfig Blitz3Plus0 => new TimeControlConfig("Blitz 3+0", 180.0, 0.0);
        public static TimeControlConfig Blitz3Plus2 => new TimeControlConfig("Blitz 3+2", 180.0, 2.0);
        public static TimeControlConfig Rapid5Plus0 => new TimeControlConfig("Rápida 5+0", 300.0, 0.0);

        public bool Equals(TimeControlConfig other) =>
            Name == other.Name &&
            InitialSeconds.Equals(other.InitialSeconds) &&
            IncrementSeconds.Equals(other.IncrementSeconds);

        public override bool Equals(object obj) => obj is TimeControlConfig other && Equals(other);

        public override int GetHashCode() =>
            ((Name != null ? Name.GetHashCode() : 0) * 397) ^ InitialSeconds.GetHashCode();
    }
#endif

    /// <summary>
    /// Reloj dual de partida estilo ajedrez (con soporte de incremento Fischer).
    /// Lleva el tiempo restante de <see cref="Player.X"/> y <see cref="Player.O"/> en segundos de alta precisión.
    /// </summary>
    public sealed class MatchClock
    {
        public TimeControlConfig Config { get; private set; }
        public double RemainingSecondsX { get; private set; }
        public double RemainingSecondsO { get; private set; }
        public Player ActivePlayer { get; private set; }
        public bool IsRunning { get; private set; }
        public Player FlaggedPlayer { get; private set; }

        public event Action<Player> TimeExpired;

        public MatchClock(TimeControlConfig config)
        {
            Reset(config);
        }

        public void Reset(TimeControlConfig config)
        {
            Config = config;
            RemainingSecondsX = config.InitialSeconds;
            RemainingSecondsO = config.InitialSeconds;
            ActivePlayer = Player.X;
            IsRunning = false;
            FlaggedPlayer = Player.None;
        }

        public void Start(Player activePlayer = Player.X)
        {
            if (FlaggedPlayer != Player.None) return;
            ActivePlayer = activePlayer;
            IsRunning = true;
        }

        public void Pause()
        {
            IsRunning = false;
        }

        /// <summary>
        /// Aplica el incremento Fischer al jugador que acaba de mover y cambia el reloj activo al rival.
        /// </summary>
        public void OnMoveCompleted(Player playerWhoMoved, Player nextPlayer)
        {
            if (FlaggedPlayer != Player.None) return;

            if (IsRunning && Config.IncrementSeconds > 0)
            {
                if (playerWhoMoved == Player.X)
                {
                    RemainingSecondsX += Config.IncrementSeconds;
                }
                else if (playerWhoMoved == Player.O)
                {
                    RemainingSecondsO += Config.IncrementSeconds;
                }
            }

            ActivePlayer = nextPlayer;
            IsRunning = true;
        }

        /// <summary>
        /// Avanza el reloj activo en <paramref name="deltaSeconds"/> segundos.
        /// Retorna <c>true</c> si el tiempo del jugador activo se agotó en este tick.
        /// </summary>
        public bool Tick(double deltaSeconds)
        {
            if (!IsRunning || deltaSeconds <= 0 || FlaggedPlayer != Player.None)
            {
                return false;
            }

            if (ActivePlayer == Player.X)
            {
                RemainingSecondsX = Math.Max(0.0, RemainingSecondsX - deltaSeconds);
                if (RemainingSecondsX <= 0.0)
                {
                    RemainingSecondsX = 0.0;
                    IsRunning = false;
                    FlaggedPlayer = Player.X;
                    TimeExpired?.Invoke(Player.X);
                    return true;
                }
            }
            else if (ActivePlayer == Player.O)
            {
                RemainingSecondsO = Math.Max(0.0, RemainingSecondsO - deltaSeconds);
                if (RemainingSecondsO <= 0.0)
                {
                    RemainingSecondsO = 0.0;
                    IsRunning = false;
                    FlaggedPlayer = Player.O;
                    TimeExpired?.Invoke(Player.O);
                    return true;
                }
            }

            return false;
        }

        public double GetRemainingSeconds(Player player)
        {
            return player == Player.O ? RemainingSecondsO : RemainingSecondsX;
        }

        /// <summary>
        /// Formatea el tiempo restante estilo Lichess:
        /// - <c>MM:SS</c> cuando quedan 20 segundos o más.
        /// - <c>00:SS.d</c> (con décimas de segundo) cuando quedan menos de 20 segundos.
        /// </summary>
        public static string FormatClock(double seconds)
        {
            if (seconds <= 0.0)
            {
                return "00:00.0";
            }

            if (seconds < 20.0)
            {
                int wholeSecs = (int)Math.Floor(seconds);
                int tenths = (int)Math.Floor((seconds - wholeSecs) * 10.0);
                return $"00:{wholeSecs:D2}.{tenths}";
            }

            int totalSeconds = (int)Math.Ceiling(seconds);
            int mins = totalSeconds / 60;
            int secs = totalSeconds % 60;
            return $"{mins:D2}:{secs:D2}";
        }
    }
}
