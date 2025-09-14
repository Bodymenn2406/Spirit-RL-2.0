// All comments in English as requested
using GTANetworkAPI;
using Spirit.Core.Const;
using Spirit.Core.Entities;
using Spirit.Core.Vehicles;
using System;
using System.Drawing;

namespace Spirit.Core.Vehicles
{
    /// <summary>
    /// Simple testing commands: /veh, /v, /dv
    /// Stores last spawned vehicle on player via SetData.
    /// </summary>
    public sealed class VehicleCommands : Script
    {
        [Command("veh")]
        public void CmdVehSpawn(Player basePlayer, string modelName, int color1 = 0, int color2 = 0)
        {
            SPlayer player = basePlayer.AsSPlayer(); // oder dein eigener Getter

            uint hash;
            if (Enum.TryParse(typeof(VehicleHash), modelName, true, out var vehHash))
                hash = (uint)(VehicleHash)vehHash;
            else if (uint.TryParse(modelName, out var parsed))
                hash = parsed;
            else
                hash = NAPI.Util.GetHashKey(modelName);

            if (hash == 0)
            {
                player.NotifyError("Invalid model/hash.");
                return;
            }

            var pos = player.GetForwardPosition(3.0f);
            var heading = player.Base.Heading;

            var sVeh = VehicleManager.Create(hash, pos, heading);
            sVeh.Base.PrimaryColor = color1;
            sVeh.Base.SecondaryColor = color2;

            sVeh.OwnerId = player.CharacterId;
            sVeh.Fuel = 55f;
            sVeh.FuelMax = 60f;
            sVeh.LightState = 0;
            sVeh.Odometer = 0;

            player.NotifySuccess($"Spawned vehicle: {modelName} (hash {hash})");
        }

        [Command("fuel")]
        public void CmdFuel(Player basePlayer, string action = "check", float value = -1f)
        {
            var sp = basePlayer.AsSPlayer();
            var veh = basePlayer.Vehicle;

            // Wenn kein Vehicle → nur bei "mult" erlauben
            if (veh == null && action != "mult")
            {
                sp.NotifyError("You must be in a vehicle for this action.");
                return;
            }

            var sVeh = veh != null ? VehicleManager.Get(veh) : null;

            switch (action.ToLowerInvariant())
            {
                case "check":
                    if (sVeh == null) { sp.NotifyError("This vehicle is not managed by VehicleManager."); return; }
                    sp.NotifyInfo($"Fuel: {Math.Round(sVeh.Fuel, 1)} / {sVeh.FuelMax} L");
                    break;

                case "fill":
                    if (sVeh == null) { sp.NotifyError("This vehicle is not managed by VehicleManager."); return; }

                    float amount = value < 0 ? sVeh.FuelMax : value;
                    sVeh.Fuel = Math.Min(amount, sVeh.FuelMax);

                    var driver = sVeh.GetDriver();
                    if (driver != null && driver.Exists)
                    {
                        driver.TriggerEvent("client:veh:updateFuel", sVeh.Fuel, sVeh.FuelMax);
                    }

                    sp.NotifySuccess($"Fuel set to {sVeh.Fuel}/{sVeh.FuelMax} L");
                    break;

                case "mult":
                    if (value <= 0) value = 1.0f;
                    SVehicle.FuelMultiplier = value;
                    sp.NotifyInfo($"Fuel multiplier set to {value}x");
                    break;

                default:
                    sp.NotifyError("Usage: /fuel check | fill [amount] | mult <factor>");
                    break;
            }
        }

        [Command("setvehhealth")]
        public void CmdSetVehHealth(Player basePlayer, int health)
        {
            var sPlayer = basePlayer.AsSPlayer();
            var veh = basePlayer.Vehicle;
            if (veh == null)
            {
                sPlayer.NotifyError("You are not in a vehicle!");
                return;
            }

            var sVeh = VehicleManager.Get(veh);
            if (sVeh == null)
            {
                sPlayer.NotifyError("Vehicle not registered as SVehicle!");
                return;
            }

            if (health < 0) health = 0;
            if (health > 1000) health = 1000;

            if(health > 0) veh.SetSharedData("veh:engineDead", false);
            else if (health == 0) veh.SetSharedData("veh:engineDead", true);// falls vorher tot war
            sVeh.Health = health; // setzt auch SharedData
            sPlayer.NotifyInfo($"Engine health set to {health}");
        }

        [Command("vehdebug")]
        public void CmdVehDebug(Player basePlayer)
        {
            var sPlayer = basePlayer.AsSPlayer();
            var veh = basePlayer.Vehicle;

            if (veh == null)
            {
                sPlayer.NotifyError("You are not in a vehicle!");
                return;
            }

            var sVeh = VehicleManager.Get(veh);
            if (sVeh == null)
            {
                sPlayer.NotifyError("No SVehicle wrapper found!");
                return;
            }

            // Ausgabe aktueller Health-Werte
            sPlayer.NotifyInfo(
                $"VehDebug → Health: {sVeh.Health}, " +
                $"Body: {sVeh.LastBodyHealth:F1}, Engine: {sVeh.LastEngineHealth:F1}, Petrol: {sVeh.LastPetrolHealth:F1}");

            // Optional auch letzten Updatezeitpunkt anzeigen
            sPlayer.NotifyInfo(
                $"Last update: {sVeh.LastHealthUpdate:HH:mm:ss.fff}");
        }




    }
}
