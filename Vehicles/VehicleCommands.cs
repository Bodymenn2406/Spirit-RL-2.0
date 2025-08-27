// All comments in English as requested
using System;
using GTANetworkAPI;
using Spirit.Core.Const;
using Spirit.Core.Vehicles;

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
            // delete previous test vehicle if present
            if (basePlayer.HasData(DataKeys.LastSpawnedVehicle))
            {
                var old = basePlayer.GetData<Vehicle>(DataKeys.LastSpawnedVehicle);
                if (old != null && old.Exists)
                {
                    try { old.Delete(); } catch { NAPI.Entity.DeleteEntity(old); }
                }
                basePlayer.ResetData(DataKeys.LastSpawnedVehicle);
            }

            if (!VehicleHelpers.TryResolveVehicleModel(model, out var hash))
            {
                NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~r~Unbekanntes Fahrzeugmodell: ~w~" + model);
                return;
            }

            // spawn position a few meters ahead
            var pos = basePlayer.Position;
            var heading = basePlayer.Heading;
            float rad = (float)(Math.PI / 180.0) * heading;
            var spawnPos = new Vector3(
                pos.X + (float)Math.Cos(rad) * 3.0f,
                pos.Y + (float)Math.Sin(rad) * 3.0f,
                pos.Z + 0.2f
            );

            var veh = NAPI.Vehicle.CreateVehicle(hash, spawnPos, heading, 0, 0, plate);
            if (veh == null || !veh.Exists)
            {
                NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~r~Fahrzeug konnte nicht erstellt werden.");
                return;
            }

            veh.Dimension = basePlayer.Dimension;
            veh.EngineStatus = true;

            // link both ways using entity data
            basePlayer.SetData(DataKeys.LastSpawnedVehicle, veh);
            veh.SetData(DataKeys.SpawnedByRemoteId, basePlayer.Handle.Value);

            // seat as driver
            VehicleHelpers.SeatDriver(basePlayer, veh);

            NAPI.Chat.SendChatMessageToPlayer(
                basePlayer,
                $"~g~Spawned & entered: ~w~{model} ~g~(Hash:~w~ {hash}) ~g~Kennz.:~w~ {plate}"
            );
        }

        [Command("v")]
        public void CmdVehAlias(Player basePlayer, string model, string plate = "SPIRIT")
            => CmdVeh(basePlayer, model, plate);

        [Command("dv")]
        public void CmdDeleteVeh(Player basePlayer)
        {
            Vehicle? v = basePlayer.Vehicle;
            if (v == null || !v.Exists)
            {
                v = basePlayer.HasData(DataKeys.LastSpawnedVehicle)
                    ? basePlayer.GetData<Vehicle>(DataKeys.LastSpawnedVehicle)
                    : null;
            }

            if (v == null || !v.Exists)
            {
                NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~y~Kein Fahrzeug gefunden.");
                return;
            }

            // allow deleting only own spawned vehicles (optional but recommended)
            if (v.HasData(DataKeys.SpawnedByRemoteId))
            {
                var ownerId = v.GetData<ushort>(DataKeys.SpawnedByRemoteId);
                if (ownerId != basePlayer.Handle.Value)
                {
                    NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~r~Du kannst dieses Fahrzeug nicht löschen.");
                    return;
                }
            }

            try { v.Delete(); } catch { NAPI.Entity.DeleteEntity(v); }

            if (basePlayer.HasData(DataKeys.LastSpawnedVehicle))
            {
                var last = basePlayer.GetData<Vehicle>(DataKeys.LastSpawnedVehicle);
                if (last == null || last == v) basePlayer.ResetData(DataKeys.LastSpawnedVehicle);
            }

            NAPI.Chat.SendChatMessageToPlayer(basePlayer, "~g~Fahrzeug gelöscht.");
        }
    }
}
