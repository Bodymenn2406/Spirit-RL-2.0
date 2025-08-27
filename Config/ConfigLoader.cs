// All comments in English as requested
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Spirit.Core.Utils;

namespace Spirit.Core.Config
{
    public static class ConfigLoader
    {
        public static AppConfig Load()
        {
            // Try multiple common locations; log the effective path
            string?[] candidates =
            {
                // 1) Directory of this assembly (resource folder in RageMP)
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                // 2) Server base directory
                AppContext.BaseDirectory,
                // 3) Explicit default for bridge/resources/Core
                Path.Combine(AppContext.BaseDirectory ?? string.Empty, "bridge", "resources", "Core")
            };

            foreach (var dir in candidates)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    var path = Path.Combine(dir, "appsettings.json");
                    if (!File.Exists(path)) continue;

                    var json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new AppConfig();

                    Logger.Log("Loaded config from: " + path);
                    return cfg;
                }
                catch (Exception ex)
                {
                    Logger.Log("Config load attempt failed: " + ex.Message, Logger.Level.Warn);
                }
            }

            Logger.Log("No appsettings.json found in known locations.", Logger.Level.Warn);
            return new AppConfig();
        }
    }
}
