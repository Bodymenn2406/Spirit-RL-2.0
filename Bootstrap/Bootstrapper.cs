// All comments in English as requested
using System;
using System.Threading.Tasks;
using GTANetworkAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spirit.Core.Config;
using Spirit.Core.Services;
using Spirit.Core.Utils;
using Spirit.Data;

namespace Spirit.Core.Bootstrap
{
    /// <summary>
    /// Resource entrypoint: builds DI, loads config, runs DB health/migrations,
    /// disables auto-spawn and prints startup info.
    /// </summary>
    public sealed class Bootstrapper : Script
    {
        [ServerEvent(Event.ResourceStart)]
        public void OnResourceStart()
        {
            // 1) Load config
            var cfg = ConfigLoader.Load();

            // 2) Configure logger before first log
            var min = Enum.TryParse(cfg.Console.MinLevel, true, out Logger.Level m) ? m : Logger.Level.Info;
            Logger.Configure(cfg.Console.Colored, cfg.Console.BrandColor, min);

            if (string.IsNullOrWhiteSpace(cfg.Database.ConnectionString))
            {
                Logger.Log("Missing DB connection string in appsettings.json (Database.ConnectionString).", Logger.Level.Error);
                return;
            }

            // 3) Build DI
            var svcs = new ServiceCollection();
            svcs.AddSingleton(cfg);
            svcs.AddDbContextFactory<SpiritDbContext>(opt =>
            {
                opt.UseMySql(cfg.Database.ConnectionString, ServerVersion.AutoDetect(cfg.Database.ConnectionString),
                    b => b.MigrationsAssembly(typeof(SpiritDbContext).Assembly.FullName));
            }, ServiceLifetime.Singleton);
            svcs.AddSingleton<DiscordService>();

            var sp = svcs.BuildServiceProvider();

            // 4) Publish to SpiritHost
            SpiritHost.Config = cfg;
            SpiritHost.Services = sp;

            Logger.Log(".NET Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Logger.Log($"Spirit RL starting - {cfg.Branding.ServerName} | {cfg.Branding.Slogan}");

            // 5) DB health + optional migrations (non-blocking)
            Task.Run(async () =>
            {
                try
                {
                    var dbf = sp.GetRequiredService<IDbContextFactory<SpiritDbContext>>();
                    await using var db = await dbf.CreateDbContextAsync();

                    if (cfg.Ef.AutoMigrate)
                    {
                        await db.Database.MigrateAsync();
                        Logger.Log("EF migrations applied.", Logger.Level.Info);
                    }

                    await db.Database.OpenConnectionAsync();
                    await db.Database.CloseConnectionAsync();
                    Logger.Log("Database connection OK.", Logger.Level.Info);
                }
                catch (Exception ex)
                {
                    Logger.Log("DB init error: " + ex.Message, Logger.Level.Error);
                }
            });

            // 6) Prevent automatic spawn until our flow allows it
            NAPI.Server.SetAutoSpawnOnConnect(false);

            Logger.Log("Core resource started.");
        }
    }
}
