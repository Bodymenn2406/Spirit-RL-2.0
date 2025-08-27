// All comments in English as requested
using System;
using System.IO;

namespace Spirit.Core.Utils
{
    public static class Logger
    {
        public enum Level { Debug = 0, Info = 1, Success = 2, Warn = 3, Error = 4 }

        private static readonly object _sync = new object();

        // runtime options
        public static Level MinLevel { get; private set; } = Level.Info;
        private static bool _colored = true;
        private static ConsoleColor _brandColor = ConsoleColor.Cyan;

        private static string LogDir => Path.Combine(AppContext.BaseDirectory, "logs");
        private static string LogFile => Path.Combine(LogDir, "core_" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log");

        /// <summary>
        /// Configure console coloring and minimum level at runtime.
        /// Call this once during resource start, before the first Log().
        /// </summary>
        public static void Configure(bool colored, string brandColorName, Level minLevel = Level.Info)
        {
            _colored = colored;
            MinLevel = minLevel;

            if (!string.IsNullOrWhiteSpace(brandColorName) &&
                Enum.TryParse(brandColorName, ignoreCase: true, out ConsoleColor parsed))
            {
                _brandColor = parsed;
            }
        }

        public static void Log(string msg, Level level = Level.Info)
        {
            if (level < MinLevel) return;

            string ts = DateTime.UtcNow.ToString("HH:mm:ss");
            string plain = $"[Spirit] [{ts}] [{level}] {msg}";

            lock (_sync)
            {
                try
                {
                    if (_colored)
                    {
                        var prev = Console.ForegroundColor;

                        // [Spirit]
                        Console.ForegroundColor = _brandColor;
                        Console.Write("[Spirit] ");

                        // [HH:mm:ss]
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write($"[{ts}] ");

                        // [Level]
                        Console.ForegroundColor = LevelToColor(level);
                        Console.Write($"[{level}] ");

                        // message
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine(msg);

                        Console.ForegroundColor = prev;
                    }
                    else
                    {
                        Console.WriteLine(plain);
                    }
                }
                catch
                {
                    // If console is unavailable, silently ignore
                }

                // file log (always plain, no colors)
                try
                {
                    Directory.CreateDirectory(LogDir);
                    File.AppendAllText(LogFile, plain + Environment.NewLine);
                }
                catch
                {
                    // swallow IO issues
                }
            }
        }

        private static ConsoleColor LevelToColor(Level level) => level switch
        {
            Level.Debug => ConsoleColor.DarkGray,
            Level.Info => ConsoleColor.White,
            Level.Success => ConsoleColor.Green,
            Level.Warn => ConsoleColor.Yellow,
            Level.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };
    }
}
