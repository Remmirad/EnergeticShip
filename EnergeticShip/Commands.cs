using TerminalApi.Classes;
using static TerminalApi.TerminalApi;
using static EnergeticShip.Actions;
using UnityEngine;
using Object = UnityEngine.Object;
using System.Linq;
using Unity.Netcode;

namespace EnergeticShip
{
    internal class Commands
    {
        private const string NOT_ENOUGH_ENERGY = "Not enough energy\n";
        public static void InitCommands()
        {
            AddCommand("scalemap", new CommandInfo
            {
                Category = "other",
                Description = $"Scales map view for {SCALE_MAP.energyUsage} {EnergeticShipSystem.ENERGY_UNIT}/min.",
                DisplayTextSupplier = ScaleMap
            }, null, false);

            AddCommand("clearweather", new CommandInfo
            {
                Category = "other",
                Description = $"Cleares weather from orbited planet {DISABLE_WEATHER.energyUsage} {EnergeticShipSystem.ENERGY_UNIT}.",
                DisplayTextSupplier = ClearWeather
            }, null, false);

            AddCommand("energy", new CommandInfo
            {
                Category = "other",
                Description = "Displays the ships current energy.",
                DisplayTextSupplier = PrintShipEnergy
            }, null, false);

            AddCommand("load", new CommandInfo
            {
                Category = "other",
                Description = "Consumes a battery to load ship energy.",
                DisplayTextSupplier = LoadShipEnergy
            }, null, false);

        }

        public static string PrintShipEnergy()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                return "Ship energy: " + Plugin.EnergySystem().GetShipEnergy() + $" {EnergeticShipSystem.ENERGY_UNIT}\n";
            } else
            {
                Plugin.EnergySystem().PrintShipEnergyServerRpc();
                return "";
            }
        }

        public static string LoadShipEnergy()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                RoundManager roundManager = GameObject.FindAnyObjectByType<RoundManager>();
                GrabbableObject[] batterys = GameObject.FindObjectsOfType<GrabbableObject>().Where((obj) => obj.isInShipRoom
                    && !obj.isHeld
                    && obj.itemProperties.Equals(Plugin.BatteryItem)).ToArray();
                if (batterys.Length > 0)
                {
                    GrabbableObject battery = batterys[0];
                    roundManager.scrapCollectedThisRound.Remove(battery);
                    Object.Destroy(battery.gameObject);
                    EnergeticShipSystem system = Plugin.EnergySystem();
                    system.AddShipEnergy(50);

                    return $"Loaded battery\nShip energy is now {system.GetShipEnergy()} {EnergeticShipSystem.ENERGY_UNIT}\n";
                }
                else
                {
                    return "No battery found in ship (mustn't be held by a player)\n";
                }
            } else
            {
                Plugin.EnergySystem().LoadShipEnergyServerRpc();
                return "";
            }
        }

        public static string ScaleMap()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                if (!SCALE_MAP.EnoughEnergyToStart()) { return NOT_ENOUGH_ENERGY; }

                if (SCALE_MAP.IsActive())
                {
                    SCALE_MAP.Stop();
                    return "Disabled scaled map\n";
                }
                else
                {
                    SCALE_MAP.Start();
                    return "Enabled scaled map\n";
                }
            } else
            {
                Plugin.EnergySystem().ScaleMapServerRpc();
                return "";
            }
        }

        

        public static string ClearWeather()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                if (!DISABLE_WEATHER.EnoughEnergyToStart()) { return NOT_ENOUGH_ENERGY; }

                if (!StartOfRound.Instance.inShipPhase)
                {
                    return "Can only be run from orbit\n";
                }
                DISABLE_WEATHER.Start();
                return $"Cleared weather from {RoundManager.Instance.currentLevel.name}\n";
            } else
            {
                Plugin.EnergySystem().ClearWeatherServerRpc();
                return "";
            }
        }
    }
}
