// All comments in English as requested
using GTANetworkAPI;
using Microsoft.EntityFrameworkCore;
using Spirit.Core.Bootstrap;
using Spirit.Core.Entities;
using Spirit.Core.Players;
using Spirit.Core.Services;
using Spirit.Core.Utils;
using Spirit.Data;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Spirit.Core.Discord
{
    /// <summary>
    /// Handles OAuth2 result and errors from client + pushes UI states (retry/timeout) to WebView.
    /// All NAPI calls are marshalled to main thread via NAPI.Task.Run.
    /// </summary>
    public sealed class DiscordEvents : Script
    {
        private const int RetryIntervalMs = 5000; // 5s
        private const int MaxRetries = 12;   // ~60s total

        // Prevent duplicate redeem per player and code re-use
        private static readonly ConcurrentDictionary<int, byte> AuthBusy = new();
        private static readonly ConcurrentDictionary<int, string> LastAuthCode = new();

        // ---------- Optional: show UI + freeze on connect ----------
        [ServerEvent(Event.PlayerConnected)]
        public void OnPlayerConnected(Player p)
        {
            NAPI.Task.Run(() =>
            {
                if (p == null || !p.Exists) return;

                // Open login UI and freeze until auth succeeds
                p.TriggerEvent("client:ui:setDiscordAppId", SpiritHost.Config.Discord.ClientId);
                p.TriggerEvent("client:ui:showLogin");
                p.TriggerEvent("client:player:freeze", true);
                p.TriggerEvent("client:ui:hud:update", JsonSerializer.Serialize(new
                {
                    // TODO: replace with your config if you have UI section
                    logoUrl = "https://airlineemployeeshop.com/cdn/shop/products/spirit002roundmagnet_1200x1200.jpg?v=1617370061", // or a CDN URL
                    slogan = "Spirit RL",
                    version = "0.1.1",
                    time = DateTime.Now.ToString("HH:mm")
                }));
                PushUiState(p, new
                {
                    busy = false,
                    text = "Bitte mit Discord anmelden.",
                    hint = ""
                });
            });
        }

        // ---------- OAuth code returned by client ----------
        [RemoteEvent("client:ui:discord:code")]
        public void OnDiscordAuthCode(Player p, string authCode)
        {
            if (p == null) return;

            // Normalize code & guard against empties
            authCode = (authCode ?? string.Empty).Trim();
            if (authCode.Length < 8)
            {
                NAPI.Task.Run(() =>
                {
                    if (p == null || !p.Exists) return;
                    PushUiState(p, new { busy = false, text = "Kein gültiger Discord-Code empfangen.", hint = "Bitte erneut versuchen." });
                });
                return;
            }

            // Guard: only one in-flight redeem per player
            if (!AuthBusy.TryAdd(p.Value, 1))
                return;

            // Prevent code re-use
            if (LastAuthCode.TryGetValue(p.Value, out var prev) && prev == authCode)
            {
                AuthBusy.TryRemove(p.Value, out _);
                return;
            }
            LastAuthCode[p.Value] = authCode;

            Task.Run(async () =>
            {
                // Capture SocialClubName on main thread (never touch Player on BG thread)
                var scn = await CaptureOnMain(() =>
                {
                    if (p == null || !p.Exists) return "Player_Unknown";
                    return p.SocialClubName ?? ("Player_" + p.Handle.Value);
                });

                try
                {
                    var cfg = SpiritHost.Config;
                    var discord = SpiritHost.Get<DiscordService>();

                    // 1) Exchange code -> token -> user (HTTP off main thread)
                    var token = await discord.ExchangeCodeAsync(authCode);
                    var me = await discord.GetMeAsync(token.AccessToken);

                    // 2) Retry loop (no NAPI here)
                    bool ok = false;
                    for (int attempt = 1; attempt <= MaxRetries; attempt++)
                    {
                        var (isMember, roles) = await discord.CheckGuildMemberAsync(me.Id);

                        bool roleOk = cfg.Discord.RequiredRoleIds.Length == 0
                                      || roles.Intersect(cfg.Discord.RequiredRoleIds).Any();

                        // Build and push UI state on main thread
                        PushUiState(p, new
                        {
                            busy = true,
                            text = "Wird geprüft …",
                            hint = !isMember
                                ? ("Bitte unserem Discord-Server beitreten: " + cfg.Discord.InviteUrl)
                                : (!roleOk ? "Erforderliche Discord-Rolle fehlt. Bitte Team kontaktieren." : ""),
                            triesLeft = MaxRetries - attempt,
                            guildOk = isMember,
                            roleOk
                        });

                        if (isMember && roleOk) { ok = true; break; }
                        await Task.Delay(RetryIntervalMs);
                    }

                    if (!ok)
                    {
                        // Timeout → UI + kick on main thread
                        NAPI.Task.Run(() =>
                        {
                            if (p == null || !p.Exists) return;
                            SPlayer player = p.AsSPlayer();
                            p.TriggerEvent("client:ui:auth:timeout", "Timeout bei der Prüfung.");
                            player.NotifyError("~r~Discord-Anforderungen nicht erfüllt. Du wirst gekickt.");
                            try { p.Kick("Discord-Anforderungen nicht erfüllt (Timeout)."); } catch { }
                        });
                        return;
                    }

                    // 3) Persist link in DB (safe off main thread)
                    var dbf = SpiritHost.Get<IDbContextFactory<SpiritDbContext>>();
                    await using var db = await dbf.CreateDbContextAsync();

                    var acc = await db.Accounts.FirstAsync(a => a.SocialClubName == scn);

                    acc.DiscordId = me.Id;
                    acc.DiscordUsername = string.IsNullOrWhiteSpace(me.GlobalName) ? me.Username : me.GlobalName;
                    acc.DiscordVerifiedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    var ch = await db.Characters.FirstAsync(c => c.AccountId == acc.Id);

                    // 4) Finalize on main thread
                    NAPI.Task.Run(() =>
                    {
                        if (p == null || !p.Exists) return;

                        var sp = p.AsSPlayer();
                        sp.AccountId = acc.Id;
                        sp.CharacterId = ch.Id;
                        sp.Money = ch.Money;
                        sp.Dirty = false;

                        // Success → UI + unfreeze + close
                        p.TriggerEvent("client:ui:auth:success", JsonSerializer.Serialize(new { welcomeMsg = "Willkommen!" }));
                        p.TriggerEvent("client:player:freeze", false);

                        // Spawn & autosave
                        PlayerPersistence.FinalizeSpawn(p, ch);

                        // All comments in English as requested
                        var hud = new
                        {
                            money = ch.Money,       // TODO: replace with your current fields
                            hunger = 50,             // TODO: hook to your needs system
                            thirst = 50,             // TODO: hook to your needs system
                            time = DateTime.Now.ToString("HH:mm"),
                            logoUrl = "https://airlineemployeeshop.com/cdn/shop/products/spirit002roundmagnet_1200x1200.jpg?v=1617370061", // or a CDN URL
                            slogan = "Spirit RL",
                            version = "0.1.1"
                        };
                        p.TriggerEvent("client:ui:hud:update", JsonSerializer.Serialize(hud));

                        if (SpiritHost.Config.Gameplay.AutoSaveSeconds > 0)
                            PlayerPersistence.StartAutoSave(p.AsSPlayer(), TimeSpan.FromSeconds(SpiritHost.Config.Gameplay.AutoSaveSeconds));
                    });

                    Logger.Log($"Discord linked: {scn} -> {me.Id} ({acc.DiscordUsername})", Logger.Level.Success);
                }
                catch (Exception ex)
                {
                    Logger.Log("Discord link failed: " + ex.Message, Logger.Level.Warn);

                    // Inform UI + chat on main thread
                    NAPI.Task.Run(() =>
                    {
                        if (p == null || !p.Exists) return;

                        PushUiState(p, new
                        {
                            busy = false,
                            text = "Discord-Login fehlgeschlagen.",
                            hint = ex.Message
                        });

                        NAPI.Chat.SendChatMessageToPlayer(p, "~r~Discord-Verknüpfung fehlgeschlagen: ~w~" + ex.Message);
                        NAPI.Chat.SendChatMessageToPlayer(p, "~y~Discord beitreten: ~w~" + SpiritHost.Config.Discord.InviteUrl);
                    });
                }
                finally
                {
                    AuthBusy.TryRemove(p.Value, out _);
                }
            });
        }

        [RemoteEvent("client:ui:discord:fail")]
        public void OnDiscordAuthFail(Player p, string reason)
        {
            NAPI.Task.Run(() =>
            {
                if (p == null || !p.Exists) return;

                PushUiState(p, new
                {
                    busy = false,
                    text = "Discord-Login abgelehnt/fehlgeschlagen.",
                    hint = reason
                });

                NAPI.Chat.SendChatMessageToPlayer(p, "~r~Discord-Anfrage abgelehnt/fehlgeschlagen: ~w~" + reason);
                NAPI.Chat.SendChatMessageToPlayer(p, "~y~Bitte erneut versuchen oder unserem Discord beitreten: ~w~" + SpiritHost.Config.Discord.InviteUrl);
            });
        }

        // ---------- Helpers (always marshal NAPI to main thread) ----------

        private static void PushUiState(Player p, object payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                NAPI.Task.Run(() =>
                {
                    if (p == null || !p.Exists) return;
                    p.TriggerEvent("client:ui:auth:state", json);
                });
            }
            catch
            {
                // never throw across threads
            }
        }

        // Run a small lambda on main thread and await its result (for reading Player fields)
        private static Task<T> CaptureOnMain<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();
            NAPI.Task.Run(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }
}
