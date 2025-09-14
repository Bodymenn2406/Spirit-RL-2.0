using System.Collections.Concurrent;
using System.Threading.Tasks;
using GTANetworkAPI;
using Spirit.Core.Entities;
using Spirit.Core.Utils;

namespace Spirit.Core.Vehicles
{
    public static class VehicleManager
    {
        private static readonly ConcurrentDictionary<Vehicle, SVehicle> vehicles = new();

        public static SVehicle Create(uint model, Vector3 pos, float heading, int ownerCharId = 0)
        {
            var veh = NAPI.Vehicle.CreateVehicle(model, pos, heading, 0, 0);
            veh.EngineStatus = false;
            var sVeh = new SVehicle(veh)
            {
                OwnerId = ownerCharId,
                Fuel = 100f,
                FuelMax = 100f,
                EngineOn = false,
                LightState = 0,
                Odometer = 0,
            };

            sVeh.Base.SetSharedData("veh:fuel", sVeh.Fuel);
            sVeh.Base.SetSharedData("veh:fuelMax", sVeh.FuelMax);
            sVeh.Base.SetSharedData("veh:lightState", sVeh.LightState);
            sVeh.Base.SetSharedData("veh:odometer", sVeh.Odometer);

            vehicles[veh] = sVeh;
            return sVeh;
        }

        public static SVehicle? Get(Vehicle veh)
        {
            return vehicles.TryGetValue(veh, out var sVeh) ? sVeh : null;
        }

        public static void Remove(Vehicle veh)
        {
            vehicles.TryRemove(veh, out _);
            if (veh.Exists) veh.Delete();
        }

        public static ConcurrentDictionary<Vehicle, SVehicle> All => vehicles;

        /// <summary>
        /// Starts a background loop that decreases fuel every second for running vehicles.
        /// Call once at resource start.
        /// </summary>
        public static async void StartFuelLoop()
        {
            while (true)
            {
                try
                {
                    NAPI.Task.Run(() =>
                    {
                        foreach (var veh in vehicles.Values)
                        {
                            veh.TickFuel();
                        }
                    });
                }
                catch
                {
                    // ignore errors, keep loop alive
                }

                await Task.Delay(1000); // wait 1s before next tick
            }
        }

        public static async void StartOdometerLoop()
        {
            while (true)
            {
                try
                {
                    NAPI.Task.Run(() =>
                    {
                        foreach (var veh in vehicles.Values)
                        {
                            veh.UpdateOdometer();
                        }
                    });
                }
                catch
                {
                    // ignore errors, keep loop alive
                }

                await Task.Delay(1000); // wait 1s before next tick
            }
        }

    }
}
