// All comments in English as requested
using System; // for Array.Empty

namespace Spirit.Core.Config
{
    public sealed class AppConfig
    {
        public DatabaseConfig Database { get; set; } = new();
        public EfConfig Ef { get; set; } = new();
        public Branding Branding { get; set; } = new();
        public Gameplay Gameplay { get; set; } = new();
        public ConsoleConfig Console { get; set; } = new();

        // << add this line
        public DiscordConfig Discord { get; set; } = new();
    }

    public sealed class DiscordConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty; // optional
        public string GuildId { get; set; } = string.Empty;
        public string InviteUrl { get; set; } = string.Empty;
        public string[] RequiredRoleIds { get; set; } = Array.Empty<string>();
        public string BotToken { get; set; } = string.Empty;    // "Bot xxxxxx"
        public bool LinkRequired { get; set; } = true;
    }

    public sealed class DatabaseConfig { public string ConnectionString { get; set; } = string.Empty; }
    public sealed class EfConfig { public bool AutoMigrate { get; set; } = true; }
    public sealed class Branding { public string ServerName { get; set; } = "Spirit RL [DE][Enhanced]"; public string Slogan { get; set; } = "Legends of Spirit Reallife"; }
    public sealed class Gameplay { public int AutoSaveSeconds { get; set; } = 120; }

    public sealed class ConsoleConfig
    {
        public bool Colored { get; set; } = true;
        public string BrandColor { get; set; } = "Cyan";
        public string MinLevel { get; set; } = "Info";
    }
}
