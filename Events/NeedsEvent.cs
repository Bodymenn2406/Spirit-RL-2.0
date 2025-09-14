using System;
using System.Text.Json;
using GTANetworkAPI;
using Spirit.Core.Entities;
using Spirit.Core.Services.Needs;
using Spirit.Core.Utils;

namespace Spirit.Core.Events
{
    public class NeedsEvents : Script
    {
        [RemoteEvent("server:need:activity")]
        public void OnNeedActivity(Player player, string json)
        {
            try
            {
                var state = JsonSerializer.Deserialize<ActivityState>(json);
                if (state == null) return;
                // rate-limit on client; server just records timestamp
                state.ts = DateTime.UtcNow;
                NeedsService.UpdateActivity(player, state);
            }
            catch (Exception ex)
            {
                Logger.Log($"[Needs] activity parse failed: {ex.Message}", Logger.Level.Warn);
            }
        }

        [RemoteEvent("server:need:consume")]
        public void OnNeedConsume(Player player, string itemId)
        {
            var sp = player.AsSPlayer();
            if (sp == null) return;
            NeedsService.ApplyConsume(sp, itemId);
        }

        [ServerEvent(Event.PlayerDeath)]
        public void OnPlayerDeath(Player player, Player killer, uint reason)
        {
            SPlayer sPlayer = player.AsSPlayer();
            //NeedsService.OnRespawn(sPlayer);
        }

        [ServerEvent(Event.PlayerConnected)]
        public void OnPlayerConnected(Player player)
        {
            var sp = player.AsSPlayer();
            if (sp == null) return;
            NeedsService.SetInitialIfEmpty(sp);
        }

        [ServerEvent(Event.PlayerSpawn)]
        public void OnPlayerSpawn(Player player)
        {
            // When the player respawns after death or revive, apply respawn defaults
            var sp = player.AsSPlayer();
            NeedsService.OnRespawn(sp);
        }

        [ServerEvent(Event.PlayerDisconnected)]
        public void OnPlayerDisconnected(Player player, DisconnectionType type, string reason)
        {
            NeedsService.Remove(player);
        }
    }
}
