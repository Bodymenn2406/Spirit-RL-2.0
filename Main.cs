using GTANetworkAPI;
using Spirit.Core.Services.Needs;
using Spirit.Core.Vehicles;

namespace Spirit.Core
{
    internal class Main : Script
    {
        [ServerEvent(Event.ResourceStart)]
        public void OnResourceStart()
        {
            VehicleManager.StartFuelLoop();
            VehicleManager.StartOdometerLoop();
            NeedsService.StartLoop();
        }
    }
}
