
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Action = EnergeticShip.Actions.Action;

namespace EnergeticShip
{
    
    public class EnergeticShipSystem : NetworkBehaviour
    {
        public const string ENERGY_UNIT = "E";

        private NetworkVariable<float> shipEnergy = new NetworkVariable<float>(0f);
        private HashSet<Action> runningActions = new HashSet<Action>();

        // Used so the state is put into save files
        public bool SafetyGuardRunning = false;

        void Start()
        {
            Plugin.logger.LogInfo($"Loaded EnergeticShipSystem");
        }

        public void Save()
        {
            string saveFile = GameNetworkManager.Instance.currentSaveFileName;
            ES3.Save("ShipEnergy", (int)shipEnergy.Value, saveFile);
            ES3.Save("ScaleMap", Actions.SCALE_MAP.IsActive(), saveFile);
            ES3.Save("TargetTeleport", Actions.TARGET_TELEPORT.IsActive(), saveFile);
            ES3.Save("KeepItemsTeleport", Actions.KEEP_ITEMS_TELEPORT.IsActive(), saveFile);
            ES3.Save("SafetyGuard", Actions.SAFETY_GUARD.IsActive(), saveFile);
        }

        public void Load()
        {
            string saveFile = GameNetworkManager.Instance.currentSaveFileName;
            shipEnergy.Value = ES3.Load("ShipEnergy", saveFile, 0);
            ResumeAction("ScaleMap", Actions.SCALE_MAP, saveFile);
            ResumeAction("TargetTeleport", Actions.TARGET_TELEPORT, saveFile);
            ResumeAction("KeepItemsTeleport", Actions.KEEP_ITEMS_TELEPORT, saveFile);
            ResumeAction("SafetyGuard", Actions.SAFETY_GUARD, saveFile);
        }

        private void ResumeAction(string id, Action action, string saveFile)
        {
            if (ES3.Load(id, saveFile, false))
            {
                action.Start(true);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                gameObject.hideFlags = HideFlags.None;
            }
        }

        public void ScheduleAction(Action action)
        {
            runningActions.Add(action);
        }

        public void UnscheduleAction(Action action)
        {
            runningActions.Remove(action);
        }

        void Update()
        {
            foreach (Action action in runningActions)
            {
                action.elapsedSeconds += Time.deltaTime;
                if (action.elapsedSeconds > 60f)
                {
                    float mins = (float)Math.Floor(action.elapsedSeconds / 60f);
                    shipEnergy.Value = Math.Max(shipEnergy.Value - mins * action.energyUsage, 0f);
                    action.elapsedSeconds %= 60f;
                }
            }
            if (shipEnergy.Value == 0f)
            {
                foreach (Action action in runningActions)
                {
                    action.Stop();
                }
            }
        }

        public float GetShipEnergy()
        {
            return shipEnergy.Value;
        }
        public void AddShipEnergy(float energy)
        {
            if (!IsServer) { return; }
            
            shipEnergy.Value += energy;
        }

        [ServerRpc(RequireOwnership = false)]
        public void PrintShipEnergyServerRpc(ServerRpcParams serverRpcParams = default)
        {
            PrintToConsoleOnClient(Commands.PrintShipEnergy(), serverRpcParams);
        }

        [ServerRpc(RequireOwnership = false)]
        public void LoadShipEnergyServerRpc(ServerRpcParams serverRpcParams = default)
        {
            PrintToConsoleOnClient(Commands.LoadShipEnergy(), serverRpcParams);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ScaleMapServerRpc(ServerRpcParams serverRpcParams = default)
        {
            PrintToConsoleOnClient(Commands.ScaleMap(), serverRpcParams);
        }

        [ServerRpc(RequireOwnership = false)]
        public void TargetTeleporterServerRpc(ServerRpcParams serverRpcParams = default)
        {
            PrintToConsoleOnClient(Commands.TargetTeleporter(), serverRpcParams);
        }

        [ServerRpc(RequireOwnership = false)]
        public void KeepItemTeleporterServerRpc(ServerRpcParams serverRpcParams = default)
        {
            PrintToConsoleOnClient(Commands.KeepItemTeleporter(), serverRpcParams);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SafetyGuardServerRpc(ServerRpcParams serverRpcParams = default)
        {
            PrintToConsoleOnClient(Commands.SafetyGuard(), serverRpcParams);
        }

        [ServerRpc(RequireOwnership = false)]
        public void DisableSafetyGuardServerRpc(ServerRpcParams serverRpcParams = default)
        {
            SafetyGuardClientRpc(false);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ClearWeatherServerRpc(ServerRpcParams serverRpcParams = default)
        {
            PrintToConsoleOnClient(Commands.ClearWeather(), serverRpcParams);
        }

        [ServerRpc(RequireOwnership = false)]
        public void TargetedBlastServerRpc(ServerRpcParams serverRpcParams = default)
        {
            PrintToConsoleOnClient(Commands.TargetedBlast(), serverRpcParams);
        }


        [ServerRpc(RequireOwnership = false)]
        public void AskToBeamUpPlayerServerRpc(ServerRpcParams serverRpcParams = default)
        {
            var clientId = serverRpcParams.Receive.SenderClientId;
            Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Got request to beam up from {clientId}");
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { clientId }
                }
            };
            Actions.KeepItemsNormalTeleportCallback(clientId);

            AllowToBeamUpPlayerClientRpc(clientRpcParams);
        }

        private void PrintToConsoleOnClient(string text, ServerRpcParams serverRpcParams)
        {
            var clientId = serverRpcParams.Receive.SenderClientId;
            if (NetworkManager.ConnectedClients.ContainsKey(clientId))
            {
                ClientRpcParams clientRpcParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[] { clientId }
                    }
                };
                PrintOnConsoleClientRpc(text, clientRpcParams);
            }
        }

        [ClientRpc]
        void PrintOnConsoleClientRpc(string text, ClientRpcParams clientRpcParams = default)
        {
            Terminal terminal = TerminalApi.TerminalApi.Terminal;
            
            terminal.modifyingText = true;

            terminal.screenText.text = terminal.screenText.text.Substring(0, terminal.screenText.text.Length - terminal.textAdded);
            terminal.currentText = terminal.screenText.text;

            terminal.screenText.text = terminal.screenText.text + text;
            terminal.currentText = terminal.screenText.text;
            terminal.textAdded = 0;
        }

        [ClientRpc]
        public void ScaleMapClientRpc(float scale)
        {
            Actions.ScaleMap(scale);
        }

        [ClientRpc]
        public void ClearWeatherClientRpc()
        {
            Actions.ClearWeather();
        }

        [ClientRpc]
        public void SpawnExplosionClientRpc(Vector3 pos)
        {
            Actions.SpawnBlastExplosion(pos);
        }

        [ClientRpc]
        public void SafetyGuardClientRpc(bool active)
        {
            if (NetworkManager.Singleton.IsServer) { return; }
            if (active)
            {
                Actions.SAFETY_GUARD.Start();
            } else
            {
                Actions.SAFETY_GUARD.Stop();
            }
        }

        [ClientRpc]
        public void AdjustFlagsAfterTeleportPlayerOutClientRpc(int teleportedPlayerObj, int teleportTargetPlayerObj)
        {
            Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Adjust flags for {teleportedPlayerObj} to those of {teleportTargetPlayerObj}");
            PlayerControllerB teleportedPlayer = StartOfRound.Instance.allPlayerScripts[teleportedPlayerObj];
            PlayerControllerB teleportTargetPlayer = StartOfRound.Instance.allPlayerScripts[teleportTargetPlayerObj];
            teleportedPlayer.isInElevator = teleportTargetPlayer.isInElevator;
            teleportedPlayer.isInHangarShipRoom = teleportTargetPlayer.isInHangarShipRoom;
            teleportedPlayer.isInsideFactory = teleportTargetPlayer.isInsideFactory;
        }

        [ClientRpc]
        public void AllowToKeepSomeItemsOnTeleportClientRpc()
        {
            Actions.AllowToKeepSomeItems();
        }

        [ClientRpc]
        public void AllowToBeamUpPlayerClientRpc(ClientRpcParams clientRpcParams = default)
        {
            Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Received beam up permisson rpc");
            Actions.AllowToBeamUpPlayer();
        }

        [ClientRpc]
        public void PlayConsumeBatterySoundClientRpc()
        {
            TerminalApi.TerminalApi.Terminal.terminalAudio.PlayOneShot(Plugin.ConsumeBattery);
        }

        [ClientRpc]
        public void PlayTriggerSafetyGuardSoundClientRpc(int playerIndex)
        {
            StartOfRound.Instance.allPlayerScripts[playerIndex].statusEffectAudio.PlayOneShot(Plugin.TriggerSafetyGuard);
        }
    }
}
