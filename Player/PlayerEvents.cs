// All comments in English as requested
using System;
using System.Linq;
using System.Threading.Tasks;
using GTANetworkAPI;
using Microsoft.EntityFrameworkCore;
using Spirit.Core.Bootstrap;
using Spirit.Core.Entities;
using Spirit.Core.Players;
using Spirit.Core.Services;
using Spirit.Core.Utils;
using Spirit.Data;

namespace Spirit.Core.Players
{
	/// <summary>
	/// Player connect/disconnect event handlers.
	/// </summary>
	public sealed class PlayerEvents : Script
	{
		[ServerEvent(Event.PlayerConnected)]
		public void OnPlayerConnected(Player basePlayer)
		{
			var sp = basePlayer.AsSPlayer();

			// Capture NAPI reads on game thread
			string scn = basePlayer.SocialClubName ?? ("Player_" + basePlayer.Handle.Value);

			Task.Run(async () =>
			{
				try
				{
					var dbf = SpiritHost.Get<IDbContextFactory<SpiritDbContext>>();
					await using var db = await dbf.CreateDbContextAsync();

					// Account
					var acc = await db.Accounts.FirstOrDefaultAsync(a => a.SocialClubName == scn);
					if (acc == null)
					{
						acc = new Spirit.Data.Entities.Account { SocialClubName = scn, DisplayName = scn, CreatedAt = DateTime.UtcNow };
						db.Accounts.Add(acc);
						await db.SaveChangesAsync();
					}
					acc.LastLoginAt = DateTime.UtcNow;
					await db.SaveChangesAsync();

					// Character (single-character MVP)
					var ch = await db.Characters.FirstOrDefaultAsync(c => c.AccountId == acc.Id);
					if (ch == null)
					{
						ch = new Spirit.Data.Entities.Character
						{
							AccountId = acc.Id,
							FirstName = "Max",
							LastName = "Mustermann",
							Money = 500
						};
						db.Characters.Add(ch);
						await db.SaveChangesAsync();
					}

					// Discord link gating
					var cfg = SpiritHost.Config;
					if (cfg.Discord.LinkRequired && string.IsNullOrWhiteSpace(acc.DiscordId))
					{
						NAPI.Task.Run(() =>
						{
							if (basePlayer == null || !basePlayer.Exists) return;
							NAPI.Player.FreezePlayer(basePlayer, true);
							NAPI.ClientEvent.TriggerClientEvent(basePlayer, "sp:discord:start", cfg.Discord.ClientId);
							NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~y~Bitte verknüpfe deinen Discord-Account, um fortzufahren.");
						});
						return;
					}

					// Attach runtime state + spawn
					NAPI.Task.Run(() =>
					{
						if (basePlayer == null || !basePlayer.Exists) return;
						sp.AccountId = acc.Id;
						sp.CharacterId = ch.Id;
						sp.Money = ch.Money;
						sp.Dirty = false;
					});

					PlayerPersistence.FinalizeSpawn(basePlayer, ch);

					// Start auto-save if enabled
					if (SpiritHost.Config.Gameplay.AutoSaveSeconds > 0)
						PlayerPersistence.StartAutoSave(sp, TimeSpan.FromSeconds(SpiritHost.Config.Gameplay.AutoSaveSeconds));
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
				PlayerPersistence.StopAutoSave(sp);
				_ = PlayerPersistence.SavePlayerStateAsync(sp, force: true);

				// Optional: clean up test vehicle data key if you use it
				if (basePlayer.HasData(Spirit.Core.Const.DataKeys.LastSpawnedVehicle))
				{
					var last = basePlayer.GetData<Vehicle>(Spirit.Core.Const.DataKeys.LastSpawnedVehicle);
					if (last != null && last.Exists)
					{
						try { last.Delete(); } catch { NAPI.Entity.DeleteEntity(last); }
					}
					basePlayer.ResetData(Spirit.Core.Const.DataKeys.LastSpawnedVehicle);
				}
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
	}
}
