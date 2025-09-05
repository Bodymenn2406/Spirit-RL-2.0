// All comments in English as requested
using System;
using System.Collections.Generic;
using System.Text.Json;
using GTANetworkAPI;
using Spirit.Core.Vehicles;

namespace Spirit.Core.Entities
{
    /// <summary>
    /// Lightweight wrapper that holds DB/runtime state for a live Vehicle
    /// </summary>
    public sealed class SVehicle
    {
        public Vehicle Base { get; }
        public ushort RemoteId => Base.Handle.Value;

        // DB linkage
        public int VehicleId { get; set; }
        public int OwnerCharacterId { get; set; }

        // Runtime state
        private bool _engineOn;
        public bool EngineOn
        {
            get => _engineOn;
            set { if (_engineOn != value) { _engineOn = value; Dirty = true; } }
        }

        private int _lightState; // 0=off, 1=low, 2=high
        public int LightState
        {
            get => _lightState;
            set { if (_lightState != value) { _lightState = value; Dirty = true; SyncLightState(); } }
        }

        private float _fuel;
        public float Fuel
        {
            get => _fuel;
            set { if (Math.Abs(_fuel - value) > 0.01f) { _fuel = value; Dirty = true; } }
        }

        public List<int> InventoryItems { get; set; } = new List<int>();

        // Persistence helpers
        public bool Dirty { get; set; } = true;
        public DateTime LastSaveUtc { get; internal set; } = DateTime.MinValue;

        public SVehicle(Vehicle veh)
        {
            Base = veh;
            // defaults
            EngineOn = veh.EngineStatus;
            Fuel = 100f;
            LightState = 0;
        }

        // ==========================
        // Sync helpers
        // ==========================

        public void SyncLightState()
        {
            if (Base == null || !Base.Exists) return;
            Base.SetSharedData("veh:lightState", LightState);
        }

        public void SyncEngine()
        {
            if (Base == null || !Base.Exists) return;
            Base.EngineStatus = EngineOn;
        }

        public void SyncFuel()
        {
            if (Base == null || !Base.Exists) return;
            Base.SetSharedData("veh:fuel", Fuel);
        }

        // ==========================
        // Convenience
        // ==========================

        public void SetPosition(Vector3 pos, float heading)
        {
            if (Base == null || !Base.Exists) return;
            Base.Position = pos;
            Base.Rotation = new Vector3(0, 0, heading);
        }

        public void Delete()
        {
            if (Base == null || !Base.Exists) return;
            Base.Delete();
        }

        public void NotifyDriver(string msg)
        {
            var p = GetDriver();
            SPlayer driver = p.AsSPlayer();
            if (driver != null)
            {
                driver.NotifyInfo(msg);
            }
        }

        public Player? GetDriver()
        {
            if (Base == null || !Base.Exists) return null;

            foreach (var occ in Base.Occupants)
            {
                if (occ is Player p && p.VehicleSeat == -1) // -1 = driver seat
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
    }
}
