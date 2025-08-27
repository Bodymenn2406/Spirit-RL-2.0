// All comments in English as requested
using System;
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

        public void SendInfo(string message) => NAPI.Chat.SendChatMessageToPlayer(Base, "~g~" + message);
        public void SendError(string message) => NAPI.Chat.SendChatMessageToPlayer(Base, "~r~" + message);

        public void Spawn(float x, float y, float z, float heading)
            => NAPI.Player.SpawnPlayer(Base, new Vector3(x, y, z), heading);
    }
}
