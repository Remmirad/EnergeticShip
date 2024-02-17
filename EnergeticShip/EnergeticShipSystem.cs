
using System;
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

        public void ScheduleAction(Action action)
        {
            runningActions.Add(action);
        }

        public void UnscheduleAction(Action action)
        {
            runningActions.Remove(action);
        }

        void Start()
        {
            Plugin.logger.LogInfo($"Loaded EnergeticShipSystem");
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                gameObject.hideFlags = HideFlags.None;
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
        public void ClearWeatherServerRpc(ServerRpcParams serverRpcParams = default)
        {
            PrintToConsoleOnClient(Commands.ClearWeather(), serverRpcParams);
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
    }
}
