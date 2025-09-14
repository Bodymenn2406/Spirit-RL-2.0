// All comments in English as requested
using System;
using GTANetworkAPI;

namespace Spirit.Core.Entities
{
    public sealed class SVehicle
    {
        public Vehicle Base { get; }
        public ushort RemoteId => Base.Handle.Value;

        // DB linkage
        public int VehicleId { get; set; }
        public int OwnerId { get; set; }

        public bool EngineOn { get; set; }
        public float LightState
        {
            get => _lightState;
            set
            {
                if (_lightState != value)
                {
                    _lightState = value;
                    Dirty = true;

                    if (Base != null && Base.Exists)
                        Base.SetSharedData("veh:lightState", _lightState);
                }
            }
        }
        private float _lightState;
        public float Fuel
        {
            get => _fuel;
            set
            {
                if (Math.Abs(_fuel - value) > 0.01f)
                {
                    _fuel = value;
                    Dirty = true;

                    if (Base != null && Base.Exists)
                        Base.SetSharedData("veh:fuel", _fuel);
                }
            }
        }
        private float _fuel;

        public float FuelMax
        {
            get => _fuelMax;
            set
            {
                if (Math.Abs(_fuelMax - value) > 0.01f)
                {
                    _fuelMax = value;
                    Dirty = true;

                    if (Base != null && Base.Exists)
                        Base.SetSharedData("veh:fuelMax", _fuelMax);
                }
            }
        }
        private float _fuelMax;

        // Last speed reported by client (km/h)
        public float LastSpeedKmh { get; set; } = 0f;
        private double _odometerMeters = 0;

        public int Odometer
        {
            get => (int)Math.Floor(_odometerMeters / 1000.0);
            set
            {
                _odometerMeters = value * 1000.0;
                Dirty = true;

                if (Base != null && Base.Exists)
                    Base.SetSharedData("veh:odometer", (int)Math.Floor(_odometerMeters / 1000.0));
            }
        }

        private float _health = 1000; // our custom health, max 1000

        public float Health
        {
            get => _health;
            set
            {
                var clamped = Math.Clamp(value, 0, 1000);
                if (_health != clamped)
                {
                    _health = clamped;
                    Dirty = true;

                    if (Base != null && Base.Exists)
                    {
                        Base.SetSharedData("veh:health", _health);
                    }
                }
            }
        }
        private bool _locked = false;

        public bool Locked
        {
            get => _locked;
            set
            {
                if (_locked != value)
                {
                    _locked = value;
                    if (Base != null && Base.Exists)
                        Base.SetSharedData("veh:locked", _locked);
                }
            }
        }
        public float LastBodyHealth { get; set; } = 1000f;
        public float LastEngineHealth { get; set; } = 1000f;
        public float LastPetrolHealth { get; set; } = 1000f;
        public DateTime LastHealthUpdate { get; set; } = DateTime.MinValue;


        private DateTime _lastOdoUpdate = DateTime.UtcNow;


        // Persistence helpers
        public bool Dirty { get; set; } = true;
        public DateTime LastSaveUtc { get; internal set; } = DateTime.MinValue;
        public static float FuelMultiplier { get; set; } = 1.0f;


        public SVehicle(Vehicle veh)
        {
            Base = veh ?? throw new ArgumentNullException(nameof(veh));
            EngineOn = false;
            Odometer = 0;
        }

        public Player? GetDriver()
        {
            if (Base == null || !Base.Exists) return null;

            foreach (var occ in Base.Occupants)
            {
                if (occ is Player p && p.VehicleSeat == 0) // -1 = driver seat
                {
                    return p;
                }
            }
            return null;
        }

        public IEnumerable<Player> GetPassengers()
        {
            if (Base == null || !Base.Exists) yield break;

            foreach (var occ in Base.Occupants)
            {
                if (occ is Player p && p.VehicleSeat != -1)
                {
                    yield return p;
                }
            }
        }


        /// <summary>
        /// Calculate fuel consumption for one tick (called every second by VehicleManager).
        /// </summary>
        public void TickFuel()
        {
            if (!EngineOn || Fuel <= 0) return;

            // Idle consumption
            float consumption = 0.02f;
            // Extra consumption based on speed
            consumption += LastSpeedKmh * 0.0001f;

            consumption *= FuelMultiplier;
            Fuel -= consumption;
            if (Fuel < 0) Fuel = 0;

            Dirty = true;

            Base.SetSharedData("veh:fuel", Fuel);
            Base.SetSharedData("veh:fuelMax", FuelMax);
        }

        public void UpdateOdometer()
        {
            if (Base == null || !Base.Exists) return;

            float speedKmh = LastSpeedKmh;
            var now = DateTime.UtcNow;
            var dt = (now - _lastOdoUpdate).TotalSeconds;
            _lastOdoUpdate = now;

            if (speedKmh > 1f)
            {
                // Strecke in Metern: (km/h * 1000 / 3600) * sek
                double distanceMeters = (speedKmh * 1000.0 / 3600.0) * dt;

                _odometerMeters += distanceMeters;

                // SharedData immer als ganze km setzen
                if (Base.Exists)
                    Base.SetSharedData("veh:odometer", (int)Math.Floor(_odometerMeters / 1000.0));
            }
        }
    }
}
