// All comments in English as requested
using System;
using System.Threading;
using System.Threading.Tasks;
using GTANetworkAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spirit.Core.Config;
using Spirit.Core.Entities;
using Spirit.Core.Utils;
using Spirit.Data;
using Spirit.Data.Entities;
using Spirit.Core.Const; // DataKeys
using System.Threading.Tasks;


namespace Spirit.Core
{
    public class Main : Script
    {
        private static IServiceProvider? _services;
        private static AppConfig? _cfg;

        [ServerEvent(Event.ResourceStart)]
        public void OnResourceStart()
        {
            _cfg = ConfigLoader.Load();

            // Configure colored console BEFORE first log line
            var min = Enum.TryParse(_cfg.Console.MinLevel, true, out Logger.Level m) ? m : Logger.Level.Info;
            Logger.Configure(_cfg.Console.Colored, _cfg.Console.BrandColor, min);

            if (string.IsNullOrWhiteSpace(_cfg.Database.ConnectionString))
            {
                Logger.Log("Missing DB connection string in appsettings.json (Database.ConnectionString).", Logger.Level.Error);
                return;
            }

            var sc = new ServiceCollection();
            sc.AddDbContextFactory<SpiritDbContext>(opt =>
            {
                opt.UseMySql(_cfg.Database.ConnectionString, ServerVersion.AutoDetect(_cfg.Database.ConnectionString),
                    b => b.MigrationsAssembly(typeof(SpiritDbContext).Assembly.FullName));
            }, ServiceLifetime.Singleton);

            _services = sc.BuildServiceProvider();

            Logger.Log(".NET Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Logger.Log("Spirit RL starting - " + _cfg.Branding.ServerName + " | " + _cfg.Branding.Slogan);

            // Non-blocking DB health + optional migrations
            Task.Run(async () =>
            {
                try
                {
                    var dbf = _services!.GetRequiredService<IDbContextFactory<SpiritDbContext>>();
                    await using var db = await dbf.CreateDbContextAsync();
                    if (_cfg!.Ef.AutoMigrate)
                    {
                        await db.Database.MigrateAsync();
                        Logger.Log("EF migrations applied.");
                    }
                    await db.Database.OpenConnectionAsync();
                    await db.Database.CloseConnectionAsync();
                    Logger.Log("Database connection OK.");
                }
                catch (Exception ex)
                {
                    Logger.Log("DB init error: " + ex.Message, Logger.Level.Error);
                }
            });

            Logger.Log("Core resource started.");
        }

        [ServerEvent(Event.PlayerConnected)]
        public void OnPlayerConnected(Player basePlayer) // NOT async
        {
            var sp = basePlayer.AsSPlayer(); // wrapper (cheap)

            // --- CAPTURE ALL NAPI DATA ON GAME THREAD FIRST ---
            // Reading SocialClubName outside the game thread triggers the error you saw.
            string scn = basePlayer.SocialClubName ?? ("Player_" + basePlayer.Handle.Value);
            ushort remoteId = basePlayer.Handle.Value;

            // Do DB work off the game thread
            Task.Run(async () =>
            {
                try
                {
                    var dbf = _services!.GetRequiredService<IDbContextFactory<SpiritDbContext>>();
                    await using var db = await dbf.CreateDbContextAsync();

                    // 1) Account by captured SocialClubName
                    var acc = await db.Accounts.FirstOrDefaultAsync(a => a.SocialClubName == scn);
                    if (acc == null)
                    {
                        acc = new Account { SocialClubName = scn, DisplayName = scn, CreatedAt = DateTime.UtcNow };
                        db.Accounts.Add(acc);
                        await db.SaveChangesAsync();
                    }
                    acc.LastLoginAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    // 2) Character
                    var ch = await db.Characters.FirstOrDefaultAsync(c => c.AccountId == acc.Id);
                    if (ch == null)
                    {
                        ch = new Character
                        {
                            AccountId = acc.Id,
                            FirstName = "Max",
                            LastName = "Mustermann",
                            Money = 500
                        };
                        db.Characters.Add(ch);
                        await db.SaveChangesAsync();
                    }

                    // Snapshot for game-thread work
                    var spawnX = ch.PosX; var spawnY = ch.PosY; var spawnZ = ch.PosZ; var heading = ch.Heading;
                    var accountId = acc.Id; var characterId = ch.Id; var money = ch.Money;

                    // 3) Back to game thread for all NAPI calls
                    NAPI.Task.Run(() =>
                    {
                        if (basePlayer == null || !basePlayer.Exists) return;

                        sp.AccountId = accountId;
                        sp.CharacterId = characterId;
                        sp.Money = money;
                        sp.Dirty = false;

                        sp.Spawn(spawnX, spawnY, spawnZ, heading);
                        sp.SendInfo("Willkommen auf Spirit RL — Legends of Spirit Reallife.");

                        if (_cfg!.Gameplay.AutoSaveSeconds > 0)
                            StartAutoSave(sp, TimeSpan.FromSeconds(_cfg.Gameplay.AutoSaveSeconds));
                    });
                }
                catch (Exception ex)
                {
                    Logger.Log("OnPlayerConnected error: " + ex.Message, Logger.Level.Error);
                }
            });
        }


        [ServerEvent(Event.PlayerDisconnected)]
        public void OnPlayerDisconnected(Player basePlayer, DisconnectionType type, string reason)
        {
            var sp = basePlayer.AsSPlayer();

            try
            {
                StopAutoSave(sp);
                // final save (fire-and-forget)
                _ = SavePlayerStateAsync(sp, force: true);
            }
            catch (Exception ex)
            {
                Logger.Log("OnPlayerDisconnected error: " + ex.Message, Logger.Level.Error);
            }
            finally
            {
                SPlayerRegistry.Remove(basePlayer);
            }
        }

        // --- Auto-save management ------------------------------------------------

        private static void StartAutoSave(SPlayer sp, TimeSpan interval)
        {
            StopAutoSave(sp); // ensure single loop

            sp.AutoSaveCts = new CancellationTokenSource();
            var token = sp.AutoSaveCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(interval, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested) break;

                        await SavePlayerStateAsync(sp, force: false).ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException) { /* normal */ }
                catch (Exception ex)
                {
                    Logger.Log("AutoSave loop error for " + sp.RemoteId + ": " + ex.Message, Logger.Level.Error);
                }
            }, token);
        }

        private static void StopAutoSave(SPlayer sp)
        {
            try
            {
                sp.AutoSaveCts?.Cancel();
                sp.AutoSaveCts?.Dispose();
                sp.AutoSaveCts = null;
            }
            catch { /* ignore */ }
        }

        // --- Persistence core ----------------------------------------------------

        private static async Task SavePlayerStateAsync(SPlayer sp, bool force)
        {
            try
            {
                // Skip if nothing changed recently (but allow forced saves)
                if (!force && !sp.Dirty) return;

                // Read position/heading from main thread
                var tcs = new TaskCompletionSource<(Vector3 pos, float heading)>();
                NAPI.Task.Run(() =>
                {
                    try
                    {
                        var pos = sp.Base.Position;
                        float heading = sp.Base.Heading; // works on current bridge
                        tcs.TrySetResult((pos, heading));
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
                var data = await tcs.Task.ConfigureAwait(false);

                var dbf = _services!.GetRequiredService<IDbContextFactory<SpiritDbContext>>();
                await using var db = await dbf.CreateDbContextAsync().ConfigureAwait(false);

                var ch = await db.Characters.FirstOrDefaultAsync(c => c.Id == sp.CharacterId).ConfigureAwait(false);
                if (ch == null) return;

                ch.PosX = data.pos.X;
                ch.PosY = data.pos.Y;
                ch.PosZ = data.pos.Z;
                ch.Heading = data.heading;
                ch.Money = sp.Money;

                await db.SaveChangesAsync().ConfigureAwait(false);

                sp.Dirty = false;
                sp.LastSaveUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Log("SavePlayerState error for " + sp.RemoteId + ": " + ex.Message, Logger.Level.Error);
            }
        }

        // --- Dev command to force save ------------------------------------------

        [Command("save")]
        public async void CmdSave(Player basePlayer)
        {
            var sp = basePlayer.AsSPlayer();
            await SavePlayerStateAsync(sp, force: true);
            sp.SendInfo("Dein Charakter wurde gespeichert.");
        }

        private static bool TryResolveVehicleModel(string input, out uint modelHash)
        {
            modelHash = 0;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            // numeric hash?
            if (uint.TryParse(input, out var parsed))
            {
                modelHash = parsed;
                return true;
            }

            // enum name? (VehicleHash.Sultan etc.)
            if (Enum.TryParse(typeof(VehicleHash), input, ignoreCase: true, out var enumValue))
            {
                modelHash = (uint)(VehicleHash)enumValue!;
                return true;
            }

            // joaat from model string (e.g. "sultan")
            try
            {
                modelHash = NAPI.Util.GetHashKey(input.ToLowerInvariant());
                return modelHash != 0;
            }
            catch
            {
                return false;
            }
        }

        // Spawnt Fahrzeug vor dem Spieler; löscht altes, setzt Spieler als Fahrer hinein
        [Command("veh")]
        public void CmdVeh(Player basePlayer, string model, string plate = "SPIRIT")
        {
            // delete previous test vehicle if stored on the player
            if (basePlayer.HasData(DataKeys.LastSpawnedVehicle))
            {
                var old = basePlayer.GetData<GTANetworkAPI.Vehicle>(DataKeys.LastSpawnedVehicle);
                if (old != null && old.Exists)
                {
                    try { old.Delete(); } catch { NAPI.Entity.DeleteEntity(old); }
                }
                basePlayer.ResetData(DataKeys.LastSpawnedVehicle);
            }

            if (!TryResolveVehicleModel(model, out var hash))
            {
                NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~r~Unbekanntes Fahrzeugmodell: ~w~" + model);
                return;
            }

            // spawn position a few meters in front of the player
            var pos = basePlayer.Position;
            var heading = basePlayer.Heading;
            float rad = (float)(Math.PI / 180.0) * heading;
            var spawnPos = new Vector3(
                pos.X + (float)Math.Cos(rad) * 3.0f,
                pos.Y + (float)Math.Sin(rad) * 3.0f,
                pos.Z + 0.2f
            );

            var veh = NAPI.Vehicle.CreateVehicle(hash, spawnPos, heading, 0, 0, plate);
            if (veh == null || !veh.Exists)
            {
                NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~r~Fahrzeug konnte nicht erstellt werden.");
                return;
            }

            veh.Dimension = basePlayer.Dimension;
            veh.EngineStatus = true;
            SeatDriver(basePlayer, veh);

            // link both ways using entity data
            basePlayer.SetData(DataKeys.LastSpawnedVehicle, veh);
            veh.SetData(DataKeys.SpawnedByRemoteId, basePlayer.Handle.Value);

            SeatDriver(basePlayer, veh);

            NAPI.Chat.SendChatMessageToPlayer(
                basePlayer,
                $"~g~Spawned & entered: ~w~{model} ~g~(Hash:~w~ {hash}) ~g~Kennz.:~w~ {plate}"
            );
        }

        [Command("v")]
        public void CmdVehAlias(Player basePlayer, string model, string plate = "SPIRIT")
            => CmdVeh(basePlayer, model, plate);

        [Command("dv")]
        public void CmdDeleteVeh(Player basePlayer)
        {
            // prefer current vehicle; fallback to last spawned stored on player
            GTANetworkAPI.Vehicle? v = basePlayer.Vehicle;
            if (v == null || !v.Exists)
            {
                v = basePlayer.HasData(DataKeys.LastSpawnedVehicle)
                    ? basePlayer.GetData<GTANetworkAPI.Vehicle>(DataKeys.LastSpawnedVehicle)
                    : null;
            }

            if (v == null || !v.Exists)
            {
                NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~y~Kein Fahrzeug gefunden.");
                return;
            }

            // optional ownership check: only allow deleting your own spawned vehicles
            if (v.HasData(DataKeys.SpawnedByRemoteId))
            {
                var ownerId = v.GetData<ushort>(DataKeys.SpawnedByRemoteId);
                if (ownerId != basePlayer.Handle.Value)
                {
                    NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~r~Du kannst dieses Fahrzeug nicht löschen.");
                    return;
                }
            }

            try { v.Delete(); } catch { NAPI.Entity.DeleteEntity(v); }

            // clear player data if it was pointing to this vehicle
            if (basePlayer.HasData(DataKeys.LastSpawnedVehicle))
            {
                var last = basePlayer.GetData<GTANetworkAPI.Vehicle>(DataKeys.LastSpawnedVehicle);
                if (last == null || last == v) basePlayer.ResetData(DataKeys.LastSpawnedVehicle);
            }

            NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~g~Fahrzeug gelöscht.");
        }

        // All comments in English as requested
        private static void SeatDriver(Player p, GTANetworkAPI.Vehicle v)
        {
            // Try immediately on game thread
            NAPI.Task.Run(() =>
            {
                try
                {
                    if (p == null || !p.Exists || v == null || !v.Exists) return;
                    // Driver seat in RAGE is 0
                    NAPI.Player.SetPlayerIntoVehicle(p, v, 0);
                }
                catch { /* ignore */ }
            });

            // Fallback: try again shortly after to ensure the vehicle is fully created client-side
            Task.Run(async () =>
            {
                await Task.Delay(150);
                NAPI.Task.Run(() =>
                {
                    try
                    {
                        if (p == null || !p.Exists || v == null || !v.Exists) return;
                        if (p.Vehicle == null || p.Vehicle != v)
                            NAPI.Player.SetPlayerIntoVehicle(p, v, 0);
                    }
                    catch { /* ignore */ }
                });
            });
        }



    }
}
