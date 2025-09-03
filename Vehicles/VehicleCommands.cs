// All comments in English as requested
using GTANetworkAPI;
using Spirit.Core.Const;
using Spirit.Core.Entities;
using Spirit.Core.Vehicles;
using System;

namespace Spirit.Core.Vehicles
{
    /// <summary>
    /// Simple testing commands: /veh, /v, /dv
    /// Stores last spawned vehicle on player via SetData.
    /// </summary>
    public sealed class VehicleCommands : Script
    {
        [Command("veh")]
        public void CmdVeh(Player basePlayer, string model, string plate = "SPIRIT")
        {
            SPlayer p = basePlayer.AsSPlayer();
            // delete previous test vehicle if present
            if (p.Base.HasData(DataKeys.LastSpawnedVehicle))
            {
                var old = p.Base.GetData<Vehicle>(DataKeys.LastSpawnedVehicle);
                if (old != null && old.Exists)
                {
                    try { old.Delete(); } catch { NAPI.Entity.DeleteEntity(old); }
                }
                p.Base.ResetData(DataKeys.LastSpawnedVehicle);
            }

            if (!VehicleHelpers.TryResolveVehicleModel(model, out var hash))
            {
                p.NotifyError("Unbekanntes Fahrzeugmodell: " + model);
                p.Base.TriggerEvent("client:ui:auth:timeout", "Timeout bei der Prüfung.");
                return;
            }

            // spawn position a few meters ahead
            var pos = p.Base.Position;
            var heading = p.Base.Heading;
            float rad = (float)(Math.PI / 180.0) * heading;
            var spawnPos = new Vector3(
                pos.X + (float)Math.Cos(rad) * 3.0f,
                pos.Y + (float)Math.Sin(rad) * 3.0f,
                pos.Z + 0.2f
            );

            var veh = NAPI.Vehicle.CreateVehicle(hash, spawnPos, heading, 0, 0, plate);
            if (veh == null || !veh.Exists)
            {
                p.NotifyError("Fahrzeug konnte nicht erstellt werden.");
                return;
            }

            veh.Dimension = p.Base.Dimension;
            veh.EngineStatus = true;

            // link both ways using entity data
            p.Base.SetData(DataKeys.LastSpawnedVehicle, veh);
            veh.SetData(DataKeys.SpawnedByRemoteId, p.Base.Handle.Value);

            // seat as driver
            VehicleHelpers.SeatDriver(p.Base, veh);

            p.NotifySuccess($"Spawned & entered: {model} (Hash: {hash}) Kennz.: {plate}");
        }

        [Command("v")]
        public void CmdVehAlias(Player basePlayer, string model, string plate = "SPIRIT")
            => CmdVeh(basePlayer, model, plate);

        [Command("dv")]
        public void CmdDeleteVeh(Player basePlayer)
        {
            var p = basePlayer.AsSPlayer();
            Vehicle? v = p.Base.Vehicle;

            if (v == null || !v.Exists)
            {
                v = p.Base.HasData(DataKeys.LastSpawnedVehicle)
                    ? p.Base.GetData<Vehicle>(DataKeys.LastSpawnedVehicle)
                    : null;
            }

            if (v == null || !v.Exists)
            {
                p.NotifyError($"Kein Fahrzeug gefunden.");
                return;
            }

            // allow deleting only own spawned vehicles (optional but recommended)
            if (v.HasData(DataKeys.SpawnedByRemoteId))
            {
                var ownerId = v.GetData<ushort>(DataKeys.SpawnedByRemoteId);
                if (ownerId != p.Base.Handle.Value)
                {
                    p.NotifyError($"Du kannst dieses fahrzeug nicht löschen.");
                    return;
                }
            }

            try { v.Delete(); } catch { NAPI.Entity.DeleteEntity(v); }

            if (p.Base.HasData(DataKeys.LastSpawnedVehicle))
            {
                var last = p.Base.GetData<Vehicle>(DataKeys.LastSpawnedVehicle);
                if (last == null || last == v) p.Base.ResetData(DataKeys.LastSpawnedVehicle);
            }

            p.NotifySuccess($"Fahrzeug gelöscht.");
        }
    }
}
