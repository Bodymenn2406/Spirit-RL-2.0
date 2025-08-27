// All comments in English as requested
using System;
using GTANetworkAPI;
using Spirit.Core.Utils;

namespace Spirit.Core.Vehicles
{
    public static class VehicleHelpers
    {
        public static bool TryResolveVehicleModel(string input, out uint modelHash)
        {
            modelHash = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            if (uint.TryParse(input, out var parsed)) { modelHash = parsed; return true; }

            if (Enum.TryParse(typeof(VehicleHash), input, true, out var enumValue))
            {
                modelHash = (uint)(VehicleHash)enumValue!;
                return true;
            }

            try
            {
                modelHash = NAPI.Util.GetHashKey(input.ToLowerInvariant());
                return modelHash != 0;
            }
            catch (Exception ex)
            {
                Logger.Log("TryResolveVehicleModel error: " + ex.Message, Logger.Level.Warn);
                return false;
            }
        }

        /// <summary>
        /// Seats player as driver (index 0). Tries immediately and again after a short delay.
        /// </summary>
        public static void SeatDriver(Player p, Vehicle v)
        {
            NAPI.Task.Run(() =>
            {
                try
                {
                    if (p == null || !p.Exists || v == null || !v.Exists) return;
                    NAPI.Player.SetPlayerIntoVehicle(p, v, 0);
                }
                catch { /* ignore */ }
            });

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(150);
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
