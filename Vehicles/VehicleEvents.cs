using GTANetworkAPI;
using Spirit.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spirit.Core.Vehicles
{
    internal class VehicleEvents : Script
    {

        [RemoteEvent("server:veh:updateSpeed")]
        public void VehUpdateSpeed(Player player, float kmh)
        {
            if (player.Vehicle == null) return;
            var sVeh = VehicleManager.Get(player.Vehicle);
            if (sVeh == null) return;

            sVeh.LastSpeedKmh = kmh;
        }

        [RemoteEvent("server:veh:setLightState")]
        public static void VehSetLightState(Player player, int state)
        {
            if (player.Vehicle == null) return;
            if (state < 0 || state > 2) state = 0;
            SVehicle? sVeh = VehicleManager.Get(player.Vehicle);
            if (sVeh == null) return;
            sVeh.LightState = state;
        }

        [RemoteEvent("server:veh:setEngine")]
        public void VehSetEngine(Player player, bool state)
        {
            if (player.Vehicle == null) return;
            var sVeh = VehicleManager.Get(player.Vehicle);
            if (sVeh == null) return;

            sVeh.EngineOn = state;
        }

        [RemoteEvent("server:veh:updateHealth")]
        public void OnVehUpdateHealth(Player player, Vehicle veh, float bodyHealth, float engineHealth, float petrolHealth)
        {
            if (veh == null || !veh.Exists) return;

            var sVeh = VehicleManager.Get(veh);
            if (sVeh == null) return;


            // Spam-Schutz
            var now = DateTime.UtcNow;
            if ((now - sVeh.LastHealthUpdate).TotalMilliseconds < 250)
                return;
            sVeh.LastHealthUpdate = now;

            try
            {
                float bodyLoss = Math.Max(0, sVeh.LastBodyHealth - bodyHealth);
                float engineLoss = Math.Max(0, sVeh.LastEngineHealth - engineHealth);

                // Update last known values
                sVeh.LastBodyHealth = bodyHealth;
                sVeh.LastEngineHealth = engineHealth;
                sVeh.LastPetrolHealth = petrolHealth;

                // Gewichtung
                const float engineFactor = 1.0f;
                const float bodyFactor = 0.15f;
                float totalLoss = (engineLoss * engineFactor) + (bodyLoss * bodyFactor);

                float oldHealth = sVeh.Health;
                sVeh.Health -= (int)Math.Floor(totalLoss);

                // Sync to all clients
                if (sVeh.Health <= 0 && veh.Exists)
                {
                    sVeh.EngineOn = false;
                    veh.SetSharedData("veh:health", 0);
                    veh.SetSharedData("veh:engineDead", true);
                }
                else
                {
                    veh.SetSharedData("veh:health", sVeh.Health);
                    veh.SetSharedData("veh:engineDead", false);
                }

                // Debugging
                var driver = sVeh.GetDriver();
                if (driver != null)
                {
                    var sPlayer = driver.AsSPlayer();
                }
            }
            catch (Exception ex)
            {
                NAPI.Util.ConsoleOutput($"[server:veh:updateHealth ERROR] {ex.Message}");
            }
        }

        [RemoteEvent("server:veh:toggleLock")]
        public void OnVehicleToggleLock(Player player, int vehId)
        {
            var sPlayer = player.AsSPlayer();

            var veh = NAPI.Pools.GetAllVehicles().FirstOrDefault(v => v.Id == vehId);
            if (veh == null || !veh.Exists)
            {
                sPlayer.NotifyError("Fahrzeug nicht gefunden.");
                return;
            }

            var sVeh = VehicleManager.Get(veh);
            if (sVeh == null) return;

            if (sVeh.OwnerId != sPlayer.CharacterId)
            {
                sPlayer.NotifyError("Dieses Fahrzeug gehört dir nicht.");
                return;
            }

            bool newState = !veh.Locked;
            veh.Locked = newState;
            veh.SetSharedData("veh:locked", newState);

            // Lock-Effect an alle in Reichweite senden
            NAPI.ClientEvent.TriggerClientEventInRange(veh.Position, 50f,
                "client:veh:lockEffect", veh.Id, newState, player.Id);

            if (newState)
                sPlayer.NotifyInfo("Fahrzeug abgeschlossen.");
            else
                sPlayer.NotifySuccess("Fahrzeug aufgeschlossen.");
        }


        [ServerEvent(Event.PlayerEnterVehicle)]
        public void OnEnterVehicle(Player basePlayer, Vehicle veh, sbyte seat)
        {
            if (seat == -1) // driver
            {
                var sVeh = VehicleManager.Get(veh);
                if (sVeh != null)
                {
                    sVeh.EngineOn = veh.EngineStatus;
                }
            }
        }

        [RemoteEvent("server:player:setSeatbelt")]
        public void OnPlayerSetSeatbelt(Player player, bool state)
        {
            player.SetSharedData("seatbeltOn", state);
        }

    }
}
