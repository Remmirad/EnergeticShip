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

            AddCommand("targettp", new CommandInfo
            {
                Category = "other",
                Description = $"Targets the inverse teleporter at the player selected on the map. {TARGET_TELEPORT.energyUsage} {EnergeticShipSystem.ENERGY_UNIT}/teleported person.",
                DisplayTextSupplier = TargetTeleporter
            }, null, false);

            AddCommand("keeptp", new CommandInfo
            {
                Category = "other",
                Description = $"Keeps two random slots when teleporting a player. {KEEP_ITEMS_TELEPORT.energyUsage} {EnergeticShipSystem.ENERGY_UNIT}/teleported person.",
                DisplayTextSupplier = KeepItemTeleporter
            }, null, false);

            AddCommand("guard", new CommandInfo
            {
                Category = "other",
                Description = $"Prevents a crew wipeout ONCE by teleporting the last player back to ship. {SAFETY_GUARD.energyUsage} {EnergeticShipSystem.ENERGY_UNIT}.",
                DisplayTextSupplier = SafetyGuard
            }, null, false);

            AddCommand("blast", new CommandInfo
            {
                Category = "other",
                Description = $"Issues a devastating blast in the center of three target beacons. {TARGETED_BLAST.energyUsage} {EnergeticShipSystem.ENERGY_UNIT}.",
                DisplayTextSupplier = TargetedBlast
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
                GrabbableObject[] batteries = GameObject.FindObjectsOfType<GrabbableObject>().Where((obj) => obj.isInShipRoom
                    && !obj.isHeld
                    && obj.itemProperties.Equals(Plugin.BatteryItem)).ToArray();
                if (batteries.Length > 0)
                {
                    GrabbableObject battery = batteries[0];
                    roundManager.scrapCollectedThisRound.Remove(battery);
                    Object.Destroy(battery.gameObject);
                    EnergeticShipSystem system = Plugin.EnergySystem();
                    system.AddShipEnergy(50);
                    system.PlayConsumeBatterySoundClientRpc();

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

        public static string TargetTeleporter()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                if (!TARGET_TELEPORT.EnoughEnergyToStart()) { return NOT_ENOUGH_ENERGY; }

                if (TARGET_TELEPORT.IsActive())
                {
                    TARGET_TELEPORT.Stop();
                    return "Disabled targeted inverse teleporter\n";
                }
                else
                {
                    TARGET_TELEPORT.Start();
                    return "Enabled targeted inverse teleporter\n";
                }
            }
            else
            {
                Plugin.EnergySystem().TargetTeleporterServerRpc();
                return "";
            }
        }

        public static string KeepItemTeleporter()
        {
            if (NetworkManager.Singleton.IsServer)
            {   
                if (KEEP_ITEMS_TELEPORT.IsActive())
                {
                    KEEP_ITEMS_TELEPORT.Stop();
                    return "Disabled keep item teleporter\n";
                }
                else
                {
                    if (!KEEP_ITEMS_TELEPORT.EnoughEnergyToStart()) { return NOT_ENOUGH_ENERGY; }
                    KEEP_ITEMS_TELEPORT.Start();
                    return "Enabled keep item teleporter\n";
                }
            }
            else
            {
                Plugin.EnergySystem().KeepItemTeleporterServerRpc();
                return "";
            }
        }

        public static string SafetyGuard()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                if (SAFETY_GUARD.IsActive())
                {
                    return "Safety guard already enabled\n";
                }
                if (!SAFETY_GUARD.EnoughEnergyToStart()) { return NOT_ENOUGH_ENERGY; }
                SAFETY_GUARD.Start();
                return "Enabled safety guard\n";
            }
            else
            {
                Plugin.EnergySystem().SafetyGuardServerRpc();
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
        public static string TargetedBlast()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                if (!TARGETED_BLAST.EnoughEnergyToStart()) { return NOT_ENOUGH_ENERGY; }

                if (StartOfRound.Instance.inShipPhase)
                {
                    return "Can only be run on planet\n";
                }
                TARGETED_BLAST.Start();
                return $"Issuing blasts...\n";
            }
            else
            {
                Plugin.EnergySystem().TargetedBlastServerRpc();
                return "";
            }
        }
    }
}
