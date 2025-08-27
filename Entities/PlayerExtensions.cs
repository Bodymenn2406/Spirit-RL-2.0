// All comments in English as requested
using GTANetworkAPI;

namespace Spirit.Core.Entities
{
    public static class PlayerExtensions
    {
        // Cast helper to obtain our wrapper from anywhere
        public static SPlayer AsSPlayer(this Player p) => SPlayerRegistry.Get(p);
    }
}
