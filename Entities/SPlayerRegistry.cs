// All comments in English as requested
using System.Collections.Concurrent;
using GTANetworkAPI;

namespace Spirit.Core.Entities
{
    // Global registry to map GTANetworkAPI.Player -> SPlayer wrapper
    public static class SPlayerRegistry
    {
        private static readonly ConcurrentDictionary<Player, SPlayer> Map = new();

        public static SPlayer Get(Player p) => Map.GetOrAdd(p, _ => new SPlayer(p));
        public static bool TryGet(Player p, out SPlayer sp) => Map.TryGetValue(p, out sp!);
        public static void Remove(Player p) => Map.TryRemove(p, out _);
    }
}
