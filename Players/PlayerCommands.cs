using GTANetworkAPI;
using Spirit.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
