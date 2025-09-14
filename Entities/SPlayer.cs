// All comments in English as requested
using System;
using System.Text.Json;
using System.Threading;
using GTANetworkAPI;

namespace Spirit.Core.Entities
{
    // Lightweight wrapper that holds DB/runtime state for a live Player
    public sealed class SPlayer
    {
        public Player Base { get; }
        public ushort RemoteId => Base.Handle.Value;

        // DB linkage
        public int AccountId { get; set; }
        public int CharacterId { get; set; }

        // Simple gameplay state
        private int _money;
        public int Money
        {
            get => _money;
            set { if (_money != value) { _money = value; Dirty = true; } }
        }

        // Persistence helpers
        public bool Dirty { get; set; } = true;
        internal CancellationTokenSource? AutoSaveCts;
        public DateTime LastSaveUtc { get; internal set; } = DateTime.MinValue;

        public SPlayer(Player player) { Base = player; }
        public void Spawn(float x, float y, float z, float heading)
            => NAPI.Player.SpawnPlayer(Base, new Vector3(x, y, z), heading);

        // =======================
        // Toast helpers (UI)
        // =======================

        private const int DefaultToastMs = 3000;
        private const int MinToastMs = 800;
        private const int MaxToastMs = 10000;

        /// <summary>
        /// sp.Notify(1, "Engine started", 2500);
        /// Types: 0=info, 1=success, 2=warning, 3=error
        /// </summary>
        public void Notify(int type, string text, int? durationMs = null)
        {
            var mapped = MapType(type);
            SendToast(mapped, text, Clamp(durationMs));
        }

        /// <summary>
        /// sp.Notify(Notifytype.Success, "Saved");
        /// </summary>
        public void Notify(NotifyType type, string text, int? durationMs = null)
        {
            SendToast(MapType((int)type), text, Clamp(durationMs));
        }

        public void NotifyInfo(string text, int? durationMs = null)
            => SendToast("info", text, Clamp(durationMs));

        public void NotifySuccess(string text, int? durationMs = null)
            => SendToast("success", text, Clamp(durationMs));

        public void NotifyWarning(string text, int? durationMs = null)
            => SendToast("warning", text, Clamp(durationMs));

        public void NotifyError(string text, int? durationMs = null)
            => SendToast("error", text, Clamp(durationMs));

        private void SendToast(string type, string text, int durationMs)
        {
            // Serialize off-thread is fine; TriggerEvent must run on main thread
            var payload = JsonSerializer.Serialize(new
            {
                type = type,
                text = text ?? string.Empty,
                durationMs = durationMs
            });

            NAPI.Task.Run(() =>
            {
                if (Base == null || !Base.Exists) return;
                Base.TriggerEvent("client:ui:toast", payload);
            });
        }

        private static string MapType(int type) => type switch
        {
            1 => "success",
            2 => "warning",
            3 => "error",
            _ => "info"
        };

        public Vector3 GetForwardPosition(float distance = 3.0f)
        {
            double heading = Base.Heading * Math.PI / 180.0;
            float x = (float)(-Math.Sin(heading)) * distance;
            float y = (float)(Math.Cos(heading)) * distance;
            return new Vector3(Base.Position.X + x, Base.Position.Y + y, Base.Position.Z);
        }

        private static int Clamp(int? ms)
        {
            var d = ms ?? DefaultToastMs;
            if (d < MinToastMs) d = MinToastMs;
            if (d > MaxToastMs) d = MaxToastMs;
            return d;
        }
    }


    /// <summary>
    /// Self-documenting kinds for Notify.
    /// </summary>
    public enum NotifyType
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }
}
