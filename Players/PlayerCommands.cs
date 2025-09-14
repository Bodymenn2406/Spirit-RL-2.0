using GTANetworkAPI;
using Spirit.Core.Config;
using Spirit.Core.Entities;
using Spirit.Core.Services.Needs;
using System.Globalization;

namespace Spirit.Core.Players
{
    internal class PlayerCommands : Script
    {
        [Command("giveweapon")]
        public void CmdGiveWeapon(Player basePlayer, string weaponName, int ammo = 100)
        {
            var sPlayer = basePlayer.AsSPlayer();

            if (string.IsNullOrWhiteSpace(weaponName))
            {
                sPlayer.NotifyError("Usage: /giveweapon [weaponName] [ammo]");
                return;
            }

            try
            {
                WeaponHash weapon;
                if (!Enum.TryParse(weaponName, true, out weapon))
                {
                    sPlayer.NotifyError($"Invalid weapon: {weaponName}");
                    return;
                }

                basePlayer.GiveWeapon(weapon, ammo);
                sPlayer.NotifySuccess($"Given {weapon} with {ammo} ammo.");
            }
            catch
            {
                sPlayer.NotifyError("Error giving weapon.");
            }
        }

        [Command("drink")]
        public void CmdDrink(Player player, string item = "water")
        {
            var sp = player.AsSPlayer();
            NeedsService.ApplyConsume(sp, item);
        }

        [Command("eat")]
        public void CmdEat(Player player, string item = "sandwich")
        {
            var sp = player.AsSPlayer();
            NeedsService.ApplyConsume(sp, item);
        }

        // /needmult <value> [seconds]
        [Command("needmult", "~y~Usage: /needmult <value> [seconds]")]
        public void CmdNeedMult(Player player, float value, int seconds = 0)
        {
            var sp = player.AsSPlayer();
            if (sp == null) return;

            if (value < 0f) value = 0f;
            if (value > 1000f) value = 1000f;

            int? ms = seconds > 0 ? seconds * 1000 : (int?)null;
            NeedsService.SetDrainMultiplier(player, value, ms);

            sp.NotifyInfo(ms.HasValue
                ? $"Needs-Drain-Multiplikator gesetzt: x{value:0.##} für {seconds}s."
                : $"Needs-Drain-Multiplikator gesetzt: x{value:0.##}");
        }

        // /needmultreset
        [Command("needmultreset")]
        public void CmdNeedMultReset(Player player)
        {
            var sp = player.AsSPlayer();
            if (sp == null) return;

            NeedsService.SetDrainMultiplier(player, 7f);
            sp.NotifyInfo("Needs-Drain-Multiplikator zurückgesetzt.");
        }
    }
}
