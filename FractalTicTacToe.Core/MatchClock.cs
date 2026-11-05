using System;
using System.Globalization;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Categoría competitiva de control de tiempo para puntuación independiente por cada una de las 4 categorías.
    /// </summary>
    public enum TimeControlCategory
    {
        Bullet = 0,
        Blitz3Plus0 = 1,
        Blitz3Plus2 = 2,
        Rapid = 3,
        Untimed = 4,
        Blitz = Blitz3Plus2
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// Configuración de control de tiempo estilo ajedrez (tiempo inicial + incremento Fischer por jugada).
    /// </summary>
    public readonly record struct TimeControlConfig(
        string Name,
        double InitialSeconds,
        double IncrementSeconds)
    {
        public bool IsUntimed => InitialSeconds <= 0.0;

        public TimeControlCategory Category
        {
            get
            {
                if (IsUntimed) return TimeControlCategory.Untimed;
                if (InitialSeconds < 180.0) return TimeControlCategory.Bullet;
                if (InitialSeconds < 300.0)
                {
                    return IncrementSeconds <= 0.0
                        ? TimeControlCategory.Blitz3Plus0
                        : TimeControlCategory.Blitz3Plus2;
                }
                return TimeControlCategory.Rapid;
            }
        }

        public string CategoryDisplayName
        {
            get
            {
                switch (Category)
                {
                    case TimeControlCategory.Bullet: return "Bullet 1+1";
                    case TimeControlCategory.Blitz3Plus0: return "Blitz 3+0";
                    case TimeControlCategory.Blitz3Plus2: return "Blitz 3+2";
                    case TimeControlCategory.Rapid: return "Rápida 5+0";
                    default: return "Sin Tiempo";
                }
            }
        }

        public static TimeControlConfig Unlimited => new TimeControlConfig("Sin Tiempo", 0.0, 0.0);
        public static TimeControlConfig Bullet1Plus1 => new TimeControlConfig("Bullet 1+1", 60.0, 1.0);
        public static TimeControlConfig Blitz3Plus0 => new TimeControlConfig("Blitz 3+0", 180.0, 0.0);
        public static TimeControlConfig Blitz3Plus2 => new TimeControlConfig("Blitz 3+2", 180.0, 2.0);
        public static TimeControlConfig Rapid5Plus0 => new TimeControlConfig("Rápida 5+0", 300.0, 0.0);

        public static TimeControlConfig CreateCustom(int minutes, int incrementSeconds)
        {
            if (minutes <= 0) return Unlimited;
            int safeMinutes = Math.Clamp(minutes, 1, 180);
            int safeInc = Math.Clamp(incrementSeconds, 0, 60);
            return new TimeControlConfig($"Personalizado {safeMinutes}+{safeInc}", safeMinutes * 60.0, safeInc);
        }

        public string ToCode()
        {
            if (IsUntimed) return "0+0";
            int mins = (int)Math.Round(InitialSeconds / 60.0);
            int inc = (int)Math.Round(IncrementSeconds);
            return $"{mins}+{inc}";
        }

        public static TimeControlConfig FromCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return Blitz3Plus2;
            string clean = code.Trim();
            switch (clean)
            {
                case "0+0": return Unlimited;
                case "1+1": return Bullet1Plus1;
                case "3+0": return Blitz3Plus0;
                case "3+2": return Blitz3Plus2;
                case "5+0": return Rapid5Plus0;
                default:
                    string[] parts = clean.Split('+');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) &&
                        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int inc))
                    {
                        if (m <= 0) return Unlimited;
                        int safeM = Math.Clamp(m, 1, 180);
                        int safeInc = Math.Clamp(inc, 0, 60);
                        return new TimeControlConfig($"{safeM}+{safeInc}", safeM * 60.0, safeInc);
                    }
                    return Blitz3Plus2;
            }
        }
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
        public bool IsUntimed => InitialSeconds <= 0.0;

        public TimeControlCategory Category
        {
            get
            {
                if (IsUntimed) return TimeControlCategory.Untimed;
                if (InitialSeconds < 180.0) return TimeControlCategory.Bullet;
                if (InitialSeconds < 300.0)
                {
                    return IncrementSeconds <= 0.0
                        ? TimeControlCategory.Blitz3Plus0
                        : TimeControlCategory.Blitz3Plus2;
                }
                return TimeControlCategory.Rapid;
            }
        }

        public string CategoryDisplayName
        {
            get
            {
                switch (Category)
                {
                    case TimeControlCategory.Bullet: return "Bullet 1+1";
                    case TimeControlCategory.Blitz3Plus0: return "Blitz 3+0";
                    case TimeControlCategory.Blitz3Plus2: return "Blitz 3+2";
                    case TimeControlCategory.Rapid: return "Rápida 5+0";
                    default: return "Sin Tiempo";
                }
            }
        }

        public TimeControlConfig(string name, double initialSeconds, double incrementSeconds)
        {
            Name = name;
            InitialSeconds = initialSeconds;
            IncrementSeconds = incrementSeconds;
        }

        public static TimeControlConfig Unlimited => new TimeControlConfig("Sin Tiempo", 0.0, 0.0);
        public static TimeControlConfig Bullet1Plus1 => new TimeControlConfig("Bullet 1+1", 60.0, 1.0);
        public static TimeControlConfig Blitz3Plus0 => new TimeControlConfig("Blitz 3+0", 180.0, 0.0);
        public static TimeControlConfig Blitz3Plus2 => new TimeControlConfig("Blitz 3+2", 180.0, 2.0);
        public static TimeControlConfig Rapid5Plus0 => new TimeControlConfig("Rápida 5+0", 300.0, 0.0);

        public static TimeControlConfig CreateCustom(int minutes, int incrementSeconds)
        {
            if (minutes <= 0) return Unlimited;
            int safeMinutes = Math.Min(180, Math.Max(1, minutes));
            int safeInc = Math.Min(60, Math.Max(0, incrementSeconds));
            return new TimeControlConfig($"Personalizado {safeMinutes}+{safeInc}", safeMinutes * 60.0, safeInc);
        }

        public string ToCode()
        {
            if (IsUntimed) return "0+0";
            int mins = (int)Math.Round(InitialSeconds / 60.0);
            int inc = (int)Math.Round(IncrementSeconds);
            return $"{mins}+{inc}";
        }

        public static TimeControlConfig FromCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return Blitz3Plus2;
            string clean = code.Trim();
            switch (clean)
            {
                case "0+0": return Unlimited;
                case "1+1": return Bullet1Plus1;
                case "3+0": return Blitz3Plus0;
                case "3+2": return Blitz3Plus2;
                case "5+0": return Rapid5Plus0;
                default:
                    string[] parts = clean.Split('+');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) &&
                        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int inc))
                    {
                        if (m <= 0) return Unlimited;
                        int safeM = Math.Min(180, Math.Max(1, m));
                        int safeInc = Math.Min(60, Math.Max(0, inc));
                        return new TimeControlConfig($"{safeM}+{safeInc}", safeM * 60.0, safeInc);
                    }
                    return Blitz3Plus2;
            }
        }

        public bool Equals(TimeControlConfig other) =>
            InitialSeconds.Equals(other.InitialSeconds) &&
            IncrementSeconds.Equals(other.IncrementSeconds);

        public override bool Equals(object obj) => obj is TimeControlConfig other && Equals(other);

        public override int GetHashCode() =>
            (InitialSeconds.GetHashCode() * 397) ^ IncrementSeconds.GetHashCode();
    }
#endif

    /// <summary>
    /// Reloj dual de partida estilo ajedrez con soporte de:
    /// - Incremento Fischer por jugada.
    /// - Modo sin límite de tiempo (<see cref="TimeControlConfig.Unlimited"/>) para partidas contra IA.
    /// - Cuenta regresiva inicial de 30 segundos para realizar el primer movimiento en partidas en línea (estilo Lichess).
    /// </summary>
    public sealed class MatchClock
    {
        public const double FirstMoveAbortLimitSeconds = 30.0;

        public TimeControlConfig Config { get; private set; }
        public double RemainingSecondsX { get; private set; }
        public double RemainingSecondsO { get; private set; }
        public Player ActivePlayer { get; private set; }
        public bool IsRunning { get; private set; }
        public Player FlaggedPlayer { get; private set; }

        /// <summary>
        /// Indica si la partida está esperando el primer movimiento con una cuenta regresiva de 30s antes de arrancar el reloj principal.
        /// </summary>
        public bool IsWaitingForFirstMove { get; private set; }
        public double FirstMoveSecondsRemaining { get; private set; }
        public bool IsAbortedByFirstMoveTimeout { get; private set; }

        public event Action<Player> TimeExpired;
        public event Action FirstMoveAbortExpired;

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
            IsWaitingForFirstMove = false;
            FirstMoveSecondsRemaining = FirstMoveAbortLimitSeconds;
            IsAbortedByFirstMoveTimeout = false;
        }

        public void Start(Player activePlayer = Player.X)
        {
            if (FlaggedPlayer != Player.None || Config.IsUntimed) return;
            IsWaitingForFirstMove = false;
            ActivePlayer = activePlayer;
            IsRunning = true;
        }

        /// <summary>
        /// Inicia la ventana de 30 segundos para realizar el primer movimiento de la partida sin descontar aún del reloj principal.
        /// </summary>
        public void StartFirstMoveCountdown(double limitSeconds = FirstMoveAbortLimitSeconds)
        {
            if (Config.IsUntimed) return;
            IsRunning = false;
            ActivePlayer = Player.X;
            IsWaitingForFirstMove = true;
            FirstMoveSecondsRemaining = limitSeconds;
            IsAbortedByFirstMoveTimeout = false;
        }

        public void Pause()
        {
            IsRunning = false;
            IsWaitingForFirstMove = false;
        }

        /// <summary>
        /// Aplica el incremento Fischer al jugador que acaba de mover (si el reloj principal ya estaba corriendo)
        /// y activa el reloj principal para el siguiente jugador.
        /// </summary>
        public void OnMoveCompleted(Player playerWhoMoved, Player nextPlayer)
        {
            if (FlaggedPlayer != Player.None || IsAbortedByFirstMoveTimeout || Config.IsUntimed)
            {
                return;
            }

            if (IsWaitingForFirstMove)
            {
                // El primer movimiento cancela el temporizador de aborto de 30s y arranca el reloj principal
                IsWaitingForFirstMove = false;
                ActivePlayer = nextPlayer;
                IsRunning = true;
                return;
            }

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
        /// Avanza el temporizador de primer movimiento (30s) o el reloj principal activo en <paramref name="deltaSeconds"/> segundos.
        /// Retorna <c>true</c> si se agotó ya sea el tiempo de primer movimiento o el reloj del jugador activo.
        /// </summary>
        public bool Tick(double deltaSeconds)
        {
            if (deltaSeconds <= 0 || Config.IsUntimed || FlaggedPlayer != Player.None || IsAbortedByFirstMoveTimeout)
            {
                return false;
            }

            if (IsWaitingForFirstMove)
            {
                FirstMoveSecondsRemaining = Math.Max(0.0, FirstMoveSecondsRemaining - deltaSeconds);
                if (FirstMoveSecondsRemaining <= 0.0)
                {
                    FirstMoveSecondsRemaining = 0.0;
                    IsWaitingForFirstMove = false;
                    IsAbortedByFirstMoveTimeout = true;
                    FirstMoveAbortExpired?.Invoke();
                    return true;
                }
                return false;
            }

            if (!IsRunning)
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
        /// Formatea el tiempo restante:
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
