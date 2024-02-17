using BepInEx;
using System.IO;
using System.Reflection;

using static TerminalApi.TerminalApi;
using UnityEngine;
using Unity.Netcode;

namespace EnergeticShip
{
    [BepInPlugin("EnergeticShip", "EnergeticShip", "0.1.0")]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    [BepInDependency("atomic.terminalapi", MinimumDependencyVersion: "1.5.0")]
    public class Plugin : BaseUnityPlugin
    {

    private static GameObject energeticShipSystemObjectPrefab;

        private static AssetBundle assetBundle;
        public static BepInEx.Logging.ManualLogSource logger;
        public static Item BatteryItem;
        private void Awake()
        {
            logger = Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            RunNetCodeSetup();

            LoadAssetBundle();

            RegisterBattery();

            Commands.InitCommands();
           
            On.StartOfRound.Start += RegisterEnergyShipSystem;

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
                energyShipSystem = GameObject.FindObjectOfType<EnergeticShipSystem>();
            }
            Plugin.logger.LogInfo($"Got System '{energyShipSystem.GetShipEnergy()}'");
            return energyShipSystem;
        }

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

        private void RegisterBattery()
        {
            int rarityWeight = 30;
            BatteryItem = assetBundle.LoadAsset<Item>("Battery");

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(BatteryItem.spawnPrefab);
            LethalLib.Modules.Items.RegisterScrap(BatteryItem, rarityWeight, LethalLib.Modules.Levels.LevelTypes.All);

            LethalLib.Modules.Items.RegisterShopItem(BatteryItem,
                null,
                null,
                CreateTerminalNode("A battery containing pure plasma empowering the ships systems"), 425);
        }

        private void RegisterEnergyShipSystem(On.StartOfRound.orig_Start orig, StartOfRound self)
        {
            orig(self);
             
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }

            EnergeticShipSystem system = GameObject.FindObjectOfType<EnergeticShipSystem>();

            if (system == null)
            {
                GameObject energeticShipSystemObject = Instantiate(energeticShipSystemObjectPrefab);
                energeticShipSystemObject.hideFlags = HideFlags.None;
                energeticShipSystemObject.GetComponent<NetworkObject>().Spawn();

                Plugin.logger.LogInfo("EnergeticShipSystem was added to GameSystems");
            } else
            {
                Plugin.logger.LogInfo("EnergeticShipSystem is already present");
            }
        }
    }
}
