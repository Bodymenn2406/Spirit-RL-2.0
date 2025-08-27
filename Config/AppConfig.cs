// All comments in English as requested
namespace Spirit.Core.Config
{
    public sealed class AppConfig
    {
        public DatabaseConfig Database { get; set; } = new();
        public EfConfig Ef { get; set; } = new();
        public Branding Branding { get; set; } = new();
        public Gameplay Gameplay { get; set; } = new();
        public ConsoleConfig Console { get; set; } = new();   // << add
    }

    public sealed class DatabaseConfig { public string ConnectionString { get; set; } = string.Empty; }
    public sealed class EfConfig { public bool AutoMigrate { get; set; } = true; }
    public sealed class Branding { public string ServerName { get; set; } = "Spirit RL [DE][Enhanced]"; public string Slogan { get; set; } = "Legends of Spirit Reallife"; }
    public sealed class Gameplay { public int AutoSaveSeconds { get; set; } = 120; }

    public sealed class ConsoleConfig
    {
        // Enable colored console output
        public bool Colored { get; set; } = true;
        // Any valid ConsoleColor name: Gray, White, Cyan, Green, Yellow, Red, etc.
        public string BrandColor { get; set; } = "Cyan";
        // Minimum level to print (Debug < Info < Success < Warn < Error)
        public string MinLevel { get; set; } = "Info";
    }
}
