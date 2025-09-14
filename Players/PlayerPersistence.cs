// All comments in English as requested
using System;
using System.Threading;
using System.Threading.Tasks;
using GTANetworkAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spirit.Core.Bootstrap;
using Spirit.Core.Entities;
using Spirit.Core.Utils;
using Spirit.Data;

namespace Spirit.Core.Players
{
    /// <summary>
    /// Persistence helpers for SPlayer: auto-save loop and finalize spawn.
    /// </summary>
    public static class PlayerPersistence
    {
        public static void StartAutoSave(SPlayer sp, TimeSpan interval)
        {
            StopAutoSave(sp);
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

        public static void StopAutoSave(SPlayer sp)
        {
            try
            {
                sp.AutoSaveCts?.Cancel();
                sp.AutoSaveCts?.Dispose();
                sp.AutoSaveCts = null;
            }
            catch { /* ignore */ }
        }

        public static async Task SavePlayerStateAsync(SPlayer sp, bool force)
        {
            try
            {
                if (!force && !sp.Dirty) return;

                // Read live NAPI data on game thread
                var tcs = new TaskCompletionSource<(Vector3 pos, float heading, bool ok)>();
                NAPI.Task.Run(() =>
                {
                    try
                    {
                        if (sp.Base == null || !sp.Base.Exists)
                        {
                            tcs.TrySetResult((default, 0f, false));
                            return;
                        }
                        var pos = sp.Base.Position;
                        var heading = sp.Base.Heading;
                        tcs.TrySetResult((pos, heading, true));
                    }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                });
                var snap = await tcs.Task.ConfigureAwait(false);
                if (!snap.ok) return;

                // Persist with EF
                var dbf = SpiritHost.Get<IDbContextFactory<SpiritDbContext>>();
                await using var db = await dbf.CreateDbContextAsync().ConfigureAwait(false);

                var ch = await db.Characters.FirstOrDefaultAsync(c => c.Id == sp.CharacterId).ConfigureAwait(false);
                if (ch == null) return;

                ch.PosX = snap.pos.X;
                ch.PosY = snap.pos.Y;
                ch.PosZ = snap.pos.Z;
                ch.Heading = snap.heading;
                ch.Money = sp.Money;
                ch.Hunger = sp.Hunger;
                ch.Thirst = sp.Thirst;

                await db.SaveChangesAsync().ConfigureAwait(false);

                sp.Dirty = false;
                sp.LastSaveUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Log("SavePlayerState error for " + sp.RemoteId + ": " + ex.Message, Logger.Level.Error);
            }
        }

        public static void FinalizeSpawn(Player p, Spirit.Data.Entities.Character ch)
        {
            NAPI.Task.Run(() =>
            {
                if (p == null || !p.Exists) return;
                p.Dimension = 0;
                var pos = new Vector3(ch.PosX, ch.PosY, ch.PosZ);
                NAPI.Player.SpawnPlayer(p, pos, ch.Heading);
                NAPI.Chat.SendChatMessageToPlayer(p, "~g~Willkommen auf Spirit RL — Legends of Spirit Reallife.");
            });
        }
    }
}
