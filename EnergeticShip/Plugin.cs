using BepInEx;
using System.IO;
using System.Reflection;

using static TerminalApi.TerminalApi;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

namespace EnergeticShip
{
    [BepInPlugin("EnergeticShip", "EnergeticShip", "0.2.1")]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    [BepInDependency("atomic.terminalapi", MinimumDependencyVersion: "1.5.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const bool DEBUG = false;
        private static GameObject energeticShipSystemObjectPrefab;

        private static AssetBundle assetBundle;
        public static BepInEx.Logging.ManualLogSource logger;
        public static Item BatteryItem;
        public static Item TargetBeaconItem;

        public static AudioClip ConsumeBattery;
        public static AudioClip TriggerSafetyGuard;
        public static AudioClip BlastBuildup;

        private void Awake()
        {
            logger = Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            RunNetCodeSetup();

            LoadAssetBundle();

            RegisterItems();

            ConsumeBattery = assetBundle.LoadAsset<AudioClip>("battery_consumption");
            TriggerSafetyGuard = assetBundle.LoadAsset<AudioClip>("trigger_safety_guard");
            BlastBuildup = assetBundle.LoadAsset<AudioClip>("blast_buildup");

            Commands.InitCommands();
            Actions.InitActions();
           
            On.StartOfRound.Start += RegisterEnergyShipSystem;
            
            if (DEBUG) On.Terminal.Start += MONEY;

            On.GameNetworkManager.SaveGame += OnGameSave;

            energeticShipSystemObjectPrefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab("EnergeticShipSystem");
            energeticShipSystemObjectPrefab.hideFlags = HideFlags.DontSave;
            energeticShipSystemObjectPrefab.AddComponent<EnergeticShipSystem>();
            
            NetworkObject net = energeticShipSystemObjectPrefab.GetComponent<NetworkObject>();
            net.DestroyWithScene = true;
            net.DontDestroyWithOwner = true;

        }

        private static EnergeticShipSystem energyShipSystem;
        public static EnergeticShipSystem EnergySystem()
        {
            if (energyShipSystem == null)
            {
                energyShipSystem = FindObjectOfType<EnergeticShipSystem>();
            }
            return energyShipSystem;
        }

        private void MONEY(On.Terminal.orig_Start orig, Terminal self)
        {
            orig(self);
            self.groupCredits = 1000;
        }

        // Necessary to initialize generated RPCs
        private void RunNetCodeSetup()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }

        private void LoadAssetBundle()
        {
            string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            assetBundle = AssetBundle.LoadFromFile(Path.Combine(sAssemblyLocation, "energeticship_assets"));
            if (assetBundle == null)
            {
                Logger.LogError("Failed to load custom assets.");
                return;
            }
            Logger.LogInfo("Loaded asset bundle");
        }

        private void RegisterItems()
        {
            int rarityWeight = 30;
            BatteryItem = assetBundle.LoadAsset<Item>("Battery");
            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(BatteryItem.spawnPrefab);
            LethalLib.Modules.Items.RegisterScrap(BatteryItem, rarityWeight, LethalLib.Modules.Levels.LevelTypes.All);
            LethalLib.Modules.Items.RegisterShopItem(BatteryItem,
                null,
                null,
                CreateTerminalNode("A battery containing pure plasma empowering the ships systems"), 425);

            TargetBeaconItem = assetBundle.LoadAsset<Item>("TargetBeacon");
            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(TargetBeaconItem.spawnPrefab);
            LethalLib.Modules.Items.RegisterItem(TargetBeaconItem);
            LethalLib.Modules.Items.RegisterShopItem(TargetBeaconItem,
                null,
                null,
                CreateTerminalNode("A target beacon. Place three in a triangle to direct the targeted blast"), 10);
        }

        private void RegisterEnergyShipSystem(On.StartOfRound.orig_Start orig, StartOfRound self)
        {
            orig(self);
            
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }

            AllItemsList items = StartOfRound.Instance.allItemsList;
            Item bigbolt = items.itemsList.Find((item) => item.itemName.Equals("Big bolt"));
            BatteryItem.grabSFX = bigbolt.grabSFX;
            BatteryItem.dropSFX = bigbolt.dropSFX;

            TargetBeaconItem.grabSFX = bigbolt.grabSFX;
            TargetBeaconItem.dropSFX = bigbolt.dropSFX;

            EnergeticShipSystem system = FindObjectOfType<EnergeticShipSystem>();

            if (system == null)
            {
                GameObject energeticShipSystemObject = Instantiate(energeticShipSystemObjectPrefab);
                energeticShipSystemObject.hideFlags = HideFlags.None;
                energeticShipSystemObject.GetComponent<EnergeticShipSystem>().Load();
                energeticShipSystemObject.GetComponent<NetworkObject>().Spawn();

                logger.LogInfo("EnergeticShipSystem was added to GameSystems");
            } else
            {
                logger.LogInfo("EnergeticShipSystem is already present");
            }
        }

        private void OnGameSave(On.GameNetworkManager.orig_SaveGame orig, GameNetworkManager self)
        {
            orig(self);
            Plugin.EnergySystem().Save();
        }
    }
}
