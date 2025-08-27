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
using System.Linq;
using System.Threading.Tasks;

namespace Spirit.Core.Discord
{
    /// <summary>
    /// Handles OAuth2 result and errors from client.
    /// </summary>
    public sealed class DiscordEvents : Script
    {
        [RemoteEvent("sp:discord:code")]
        public void OnDiscordAuthCode(Player p, string authCode)
        {
            Task.Run(async () =>
            {
                try
                {
                    var cfg = SpiritHost.Config;
                    var discord = SpiritHost.Get<DiscordService>();

                    var token = await discord.ExchangeCodeAsync(authCode);
                    var me = await discord.GetMeAsync(token.AccessToken);

                    var (isMember, roles) = await discord.CheckGuildMemberAsync(me.Id);
                    if (!isMember)
                        throw new Exception("Discord-Mitgliedschaft fehlt. Bitte beitreten: " + cfg.Discord.InviteUrl);

                    if (cfg.Discord.RequiredRoleIds.Length > 0)
                    {
                        bool ok = roles.Intersect(cfg.Discord.RequiredRoleIds).Any();
                        if (!ok) throw new Exception("Erforderliche Discord-Rolle fehlt. Bitte kontaktiere das Team.");
                    }

                    var dbf = SpiritHost.Get<IDbContextFactory<SpiritDbContext>>();
                    await using var db = await dbf.CreateDbContextAsync();

                    var scn = p.SocialClubName ?? ("Player_" + p.Handle.Value);
                    var acc = await db.Accounts.FirstAsync(a => a.SocialClubName == scn);

                    acc.DiscordId = me.Id;
                    acc.DiscordUsername = string.IsNullOrWhiteSpace(me.GlobalName) ? me.Username : me.GlobalName;
                    acc.DiscordVerifiedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    var ch = await db.Characters.FirstAsync(c => c.AccountId == acc.Id);

                    // attach state and spawn
                    NAPI.Task.Run(() =>
                    {
                        if (p == null || !p.Exists) return;
                        var sp = p.AsSPlayer();
                        sp.AccountId = acc.Id;
                        sp.CharacterId = ch.Id;
                        sp.Money = ch.Money;
                        sp.Dirty = false;
                    });

                    PlayerPersistence.FinalizeSpawn(p, ch);

                    if (SpiritHost.Config.Gameplay.AutoSaveSeconds > 0)
                        PlayerPersistence.StartAutoSave(p.AsSPlayer(), TimeSpan.FromSeconds(SpiritHost.Config.Gameplay.AutoSaveSeconds));

                    Logger.Log($"Discord linked: {scn} -> {me.Id} ({acc.DiscordUsername})", Logger.Level.Success);
                }
                catch (Exception ex)
                {
                    Logger.Log("Discord link failed: " + ex.Message, Logger.Level.Warn);
                    NAPI.Task.Run(() =>
                    {
                        if (p == null || !p.Exists) return;
                        NAPI.Chat.SendChatMessageToPlayer(p, "~r~Discord-Verknüpfung fehlgeschlagen: ~w~" + ex.Message);
                        NAPI.Chat.SendChatMessageToPlayer(p, "~y~Discord beitreten: ~w~" + SpiritHost.Config.Discord.InviteUrl);
                    });
                }
            });
        }

        [RemoteEvent("sp:discord:fail")]
        public void OnDiscordAuthFail(Player p, string reason)
        {
            NAPI.Task.Run(() =>
            {
                if (p == null || !p.Exists) return;
                NAPI.Chat.SendChatMessageToPlayer(p, "~r~Discord-Anfrage abgelehnt/fehlgeschlagen: ~w~" + reason);
                NAPI.Chat.SendChatMessageToPlayer(p, "~y~Bitte erneut versuchen oder unserem Discord beitreten: ~w~" + SpiritHost.Config.Discord.InviteUrl);
            });
        }
    }
}
