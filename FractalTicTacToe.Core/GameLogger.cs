using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FractalTicTacToe.Core
{
    public enum GameLogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public readonly struct GameLogEntry
    {
        public DateTime TimestampUtc { get; }
        public GameLogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }

        public GameLogEntry(DateTime timestampUtc, GameLogLevel level, string category, string message)
        {
            TimestampUtc = timestampUtc;
            Level = level;
            Category = string.IsNullOrWhiteSpace(category) ? "GENERAL" : category.Trim().ToUpperInvariant();
            Message = message ?? string.Empty;
        }

        public string ToFormattedString()
        {
            string lvl;
            switch (Level)
            {
                case GameLogLevel.Debug: lvl = "DBG"; break;
                case GameLogLevel.Info: lvl = "INF"; break;
                case GameLogLevel.Warning: lvl = "WRN"; break;
                case GameLogLevel.Error: lvl = "ERR"; break;
                default: lvl = "INF"; break;
            }

            return $"[{TimestampUtc.ToLocalTime():HH:mm:ss.fff}] [{lvl}] [{Category}] {Message}";
        }

        public override string ToString() => ToFormattedString();
    }

    /// <summary>
    /// Sistema centralizado y thread-safe de bitácora (Logging) para diagnóstico de red, partidas, IA y UI.
    /// Mantiene un buffer circular en memoria y opcionalmente persiste cada evento en disco.
    /// </summary>
    public static class GameLogger
    {
        public const int DefaultMaxEntries = 400;

        private static readonly object SyncLock = new object();
        private static readonly List<GameLogEntry> Entries = new List<GameLogEntry>(DefaultMaxEntries);
        private static int _maxEntries = DefaultMaxEntries;
        private static string _logFilePath;

        public static event Action<GameLogEntry> EntryAdded;

        public static int Count
        {
            get
            {
                lock (SyncLock)
                {
                    return Entries.Count;
                }
            }
        }

        public static string LogFilePath
        {
            get
            {
                lock (SyncLock)
                {
                    return _logFilePath;
                }
            }
        }

        public static void ConfigureFileSink(string filePath, int maxMemoryEntries = DefaultMaxEntries)
        {
            lock (SyncLock)
            {
                _maxEntries = Math.Max(20, maxMemoryEntries);
                _logFilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath.Trim();

                if (!string.IsNullOrEmpty(_logFilePath))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(_logFilePath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                    }
                    catch
                    {
                        // Si el sistema de archivos no permite crear la carpeta, mantener solo en memoria
                        _logFilePath = null;
                    }
                }
            }
        }

        public static void Debug(string category, string message) => Log(GameLogLevel.Debug, category, message);
        public static void Info(string category, string message) => Log(GameLogLevel.Info, category, message);
        public static void Warn(string category, string message) => Log(GameLogLevel.Warning, category, message);
        public static void Warning(string category, string message) => Log(GameLogLevel.Warning, category, message);
        public static void Error(string category, string message) => Log(GameLogLevel.Error, category, message);

        public static void Log(GameLogLevel level, string category, string message)
        {
            var entry = new GameLogEntry(DateTime.UtcNow, level, category, message);

            lock (SyncLock)
            {
                if (Entries.Count >= _maxEntries)
                {
                    Entries.RemoveAt(0);
                }
                Entries.Add(entry);

                if (!string.IsNullOrEmpty(_logFilePath))
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, entry.ToFormattedString() + Environment.NewLine, Encoding.UTF8);
                    }
                    catch
                    {
                        // Ignorar errores de E/S en bitácora para nunca interrumpir la partida
                    }
                }
            }

            try
            {
                EntryAdded?.Invoke(entry);
            }
            catch
            {
                // Ignorar excepciones de suscriptores externos
            }
        }

        public static List<GameLogEntry> GetRecentEntries(int maxCount = 100)
        {
            lock (SyncLock)
            {
                int count = Math.Min(Math.Max(1, maxCount), Entries.Count);
                int startIndex = Entries.Count - count;
                return Entries.GetRange(startIndex, count);
            }
        }

        public static string ExportFormattedText(int maxCount = 120)
        {
            List<GameLogEntry> recent = GetRecentEntries(maxCount);
            if (recent.Count == 0)
            {
                return "Sin registros en la bitácora.";
            }

            var sb = new StringBuilder(recent.Count * 64);
            for (int i = 0; i < recent.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.Append(recent[i].ToFormattedString());
            }
            return sb.ToString();
        }

        public static void Clear()
        {
            lock (SyncLock)
            {
                Entries.Clear();
                if (!string.IsNullOrEmpty(_logFilePath) && File.Exists(_logFilePath))
                {
                    try
                    {
                        File.WriteAllText(_logFilePath, string.Empty, Encoding.UTF8);
                    }
                    catch
                    {
                        // Ignorar
                    }
                }
            }
        }
    }
}
