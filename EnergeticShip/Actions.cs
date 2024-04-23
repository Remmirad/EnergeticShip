using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EnergeticShip
{
    public class Actions
    {
        public static readonly Action SCALE_MAP = new(Action.ActionType.PassiveEffect ,() =>
        {
            Plugin.EnergySystem().ScaleMapClientRpc(2f);
        }, () =>
        {
            Plugin.EnergySystem().ScaleMapClientRpc(0.5f);
        }, 3f, true);

        public static readonly Action DISABLE_WEATHER = new(Action.ActionType.SingleAction, () => { Plugin.EnergySystem().ClearWeatherClientRpc(); }, () => { }, 25f, false);

        public static readonly Action TARGET_TELEPORT = new(Action.ActionType.PassiveEffect, () => { }, () => { }, 7f, false);

        public static readonly Action KEEP_ITEMS_TELEPORT = new(Action.ActionType.PassiveEffect, () => { }, () => { }, 15f, false);

        public static readonly Action SAFETY_GUARD = new(Action.ActionType.PassiveEffect,
            () => { Plugin.EnergySystem().SafetyGuardClientRpc(true); Plugin.EnergySystem().SafetyGuardRunning = true; },
            () => { Plugin.EnergySystem().SafetyGuardClientRpc(false); Plugin.EnergySystem().SafetyGuardRunning = false; },
            0f, false);

        public static readonly Action TARGETED_BLAST = new(Action.ActionType.SingleAction, SpawnTargetedBlasts, () => { }, 10f,false);
        const float MAX_BEACON_DISTANCE = 14f;

        private static bool SafetyRetreatActive = false;

        public class Action
        {
            public enum ActionType
            {
                PassiveEffect,
                SingleAction,
            }

            public delegate void Callback();

            public float energyUsage { get; }
            public float elapsedSeconds { get; set; }
            private bool active;
            
            private Callback startCB;
            private Callback stopCB;
            private bool needsEnergyConstantly;
            private ActionType type;
            public Action(ActionType type, Callback startCB, Callback stopCB, float energyUsage, bool needsEnergyConstantly = false)
            {
                this.type = type;
                this.startCB = startCB;
                this.stopCB = stopCB;
                this.energyUsage = energyUsage;

                this.needsEnergyConstantly = needsEnergyConstantly;
                elapsedSeconds = 0;
            }

            public bool EnoughEnergyToStart()
            {
                return Plugin.EnergySystem().GetShipEnergy() >= energyUsage;
            }

            /// <param name="resume">Used when loading save file</param>
            public void Start(bool resume = false)
            {
                if (NetworkManager.Singleton.IsServer)
                {
                    if (type == ActionType.PassiveEffect)
                    {
                        active = true;
                        if (NeedsEnergyConstantly())
                            Plugin.EnergySystem().ScheduleAction(this);
                    }
                    else if(!resume)
                    {
                        Plugin.EnergySystem().AddShipEnergy(-energyUsage);
                    }
                    startCB();
                }
                else
                {
                    if (type == ActionType.PassiveEffect)
                    {
                        active = true;
                    }
                }               
            }

            public void Stop() {
                Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Disabling effect: {this}");
                if (NetworkManager.Singleton.IsServer)
                {
                    if (type == ActionType.PassiveEffect)
                    {
                        active = false;
                        if (NeedsEnergyConstantly())
                            Plugin.EnergySystem().UnscheduleAction(this);
                    }
                    if (stopCB != null) { stopCB(); }
                } else
                {
                    if (type == ActionType.PassiveEffect)
                    {
                        active = false;
                    }
                }                    
            }

            public bool NeedsEnergyConstantly()
            {
                return needsEnergyConstantly;
            }

            public bool IsActive() { return active; }
        }

        public static void InitActions()
        {
            On.ShipTeleporter.TeleportPlayerOutWithInverseTeleporter += OnTeleportPlayerOutWithInverseTeleporter;
            On.ShipTeleporter.TeleportPlayerOutClientRpc += OnTeleportPlayerOutClientRpc;
            On.ShipTeleporter.beamUpPlayer += OnbeamUpPlayer;
            On.GameNetcodeStuff.PlayerControllerB.DropAllHeldItems += OnDropAllHeldItems;
            On.GameNetcodeStuff.PlayerControllerB.KillPlayer += OnKillPlayer;
        }

        private static void OnKillPlayer(
            On.GameNetcodeStuff.PlayerControllerB.orig_KillPlayer orig, PlayerControllerB self,
            Vector3 bodyVelocity, bool spawnBody,
            CauseOfDeath causeOfDeath, int deathAnimation)
        {
            int alive = StartOfRound.Instance.livingPlayers;

            Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Player {self} tries to die because of {causeOfDeath}. Safety guard active: {SAFETY_GUARD.IsActive()}, Players alive: {alive}");
            if (!(SAFETY_GUARD.IsActive() && alive <= 1) || causeOfDeath == CauseOfDeath.Gravity)
            {
                orig(self,bodyVelocity,spawnBody,causeOfDeath,deathAnimation);
            } else
            {
                Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Prevented death");
                if (!SafetyRetreatActive)
                {
                    Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Initiate teleport");
                    Plugin.EnergySystem().PlayTriggerSafetyGuardSoundClientRpc(PlayerScriptIndex(self));
                    StartOfRound.Instance.mapScreen.SwitchRadarTargetAndSync(PlayerRadarIndex(self));
                    GetShipTeleporter().PressTeleportButtonOnLocalClient();
                    StartOfRound.Instance.playerTeleportedEvent.AddListener(runSafeReturn);
                    SafetyRetreatActive = true;
                }
            }
        }

        private static int PlayerScriptIndex(PlayerControllerB player)
        {
            PlayerControllerB[] targets = StartOfRound.Instance.allPlayerScripts;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == player)
                {
                    return i;
                }
            }
            return -1;
        }

        private static int PlayerRadarIndex(PlayerControllerB player)
        {
            List<TransformAndName> targets = StartOfRound.Instance.mapScreen.radarTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].transform.gameObject.GetComponent<PlayerControllerB>() == player)
                {
                    return i;
                }
            }
            return -1;
        }

        private static void runSafeReturn(PlayerControllerB self)
        {
            StartOfRound.Instance.playerTeleportedEvent.RemoveListener(runSafeReturn);
            SafetyRetreatActive = false;
            SAFETY_GUARD.Stop();
            Plugin.EnergySystem().DisableSafetyGuardServerRpc();
        }

        private static ShipTeleporter GetShipTeleporter()
        {
            ShipTeleporter[] array = Object.FindObjectsOfType<ShipTeleporter>();
            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    if (!array[i].isInverseTeleporter)
                    {
                        return array[i];
                    }
                }
            }
            return null;
        }

        // Dirty hack because we cant access __rpc_exec_stage to differntiate between issuing and receiving the rpc on the server.
        private static bool currentlyRunningTeleportOnServer = false;
        private static void OnTeleportPlayerOutClientRpc(On.ShipTeleporter.orig_TeleportPlayerOutClientRpc orig, ShipTeleporter self, int playerObjToTeleport, Vector3 targetPos)
        {
            Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Intercepted TelerportPlayerOutClientRpc for {playerObjToTeleport} with pos {targetPos}");
            Vector3 newTargetPos = targetPos;
            ShipTeleporter teleporter = GetShipInverseTeleporter();
            bool adjustPlayerControllerFlags = false;
            PlayerControllerB teleportTargetPlayer = null;
            if (NetworkManager.Singleton.IsServer && !currentlyRunningTeleportOnServer)
            {
                currentlyRunningTeleportOnServer = true;

                // Used only with 
                if (KEEP_ITEMS_TELEPORT.IsActive() && KEEP_ITEMS_TELEPORT.EnoughEnergyToStart())
                {
                    PlayerControllerB playerToTeleport = StartOfRound.Instance.allPlayerScripts[playerObjToTeleport];
                    Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Activating keep items from inverse teleporter");
                    Plugin.EnergySystem().AddShipEnergy(-KEEP_ITEMS_TELEPORT.energyUsage);
                    Plugin.EnergySystem().AllowToKeepSomeItemsOnTeleportClientRpc();
                }

                if (TARGET_TELEPORT.IsActive() && TARGET_TELEPORT.EnoughEnergyToStart())
                {
                    PlayerControllerB playerToTeleport = StartOfRound.Instance.allPlayerScripts[playerObjToTeleport];
                    if ((Object)(object)StartOfRound.Instance.mapScreen.targetedPlayer == (Object)(object)playerToTeleport)
                    {
                        Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Did not teleport player {playerObjToTeleport} to himself");
                        return;
                    }

                    Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Teleporter is active and ship has enough energy");
                    Plugin.EnergySystem().AddShipEnergy(-TARGET_TELEPORT.energyUsage);
                    teleportTargetPlayer = StartOfRound.Instance.mapScreen.targetedPlayer;
                    newTargetPos = teleportTargetPlayer.transform.position;
                    Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Reroute teleport to position: " + newTargetPos);
                    adjustPlayerControllerFlags = true;
                }                
            }
            serverAllowedTeleport = true;
            orig(self, playerObjToTeleport, newTargetPos);
            if (StartOfRound.Instance.allPlayerScripts[playerObjToTeleport].IsOwner)
            {
                Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Teleported {playerObjToTeleport}");
                teleporter.TeleportPlayerOutWithInverseTeleporter(playerObjToTeleport, newTargetPos);
            }

            if (adjustPlayerControllerFlags)
            {
                Plugin.EnergySystem().AdjustFlagsAfterTeleportPlayerOutClientRpc(
                    playerObjToTeleport, Array.FindIndex(StartOfRound.Instance.allPlayerScripts, e => e == teleportTargetPlayer)
                );
            }
            if (NetworkManager.Singleton.IsServer)
            {
                currentlyRunningTeleportOnServer = false;
            }
        }

        private static ShipTeleporter GetShipInverseTeleporter()
        {
            ShipTeleporter[] array = Object.FindObjectsOfType<ShipTeleporter>();
            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    if (array[i].isInverseTeleporter)
                    {
                        return array[i];
                    }
                }
            }
            return null;
        }



        private static bool serverAllowedTeleport = false;
        private static void OnTeleportPlayerOutWithInverseTeleporter(On.ShipTeleporter.orig_TeleportPlayerOutWithInverseTeleporter orig, ShipTeleporter self, int playerObj, Vector3 targetPos)
        {
            //GetShipTeleporter().cooldownTime = 0;
            if (serverAllowedTeleport)
            {
                Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Teleport {playerObj} to {targetPos}");
                orig(self, playerObj, targetPos);
                serverAllowedTeleport = false;
            }
            else
            {
                Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Teleport BLOCKED {playerObj} to {targetPos}");
            }
        }

        // Used only with the normal teleporter
        public static void KeepItemsNormalTeleportCallback(ulong clientId)
        {
            Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: KeepItemsTeleportCallback for id {clientId}");
            if (KEEP_ITEMS_TELEPORT.IsActive() 
                && Plugin.EnergySystem().GetShipEnergy() > KEEP_ITEMS_TELEPORT.energyUsage 
                && StartOfRound.Instance.mapScreen.targetedPlayer.actualClientId == clientId)
            {
                Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Activating keep items from normal teleporter");
                Plugin.EnergySystem().AddShipEnergy(-KEEP_ITEMS_TELEPORT.energyUsage);
                Plugin.EnergySystem().AllowToKeepSomeItemsOnTeleportClientRpc();
            }
        }

        public static void AllowToBeamUpPlayer()
        {
            allowedToBeamUpPlayer = true;
        }
        private static bool allowedToBeamUpPlayer = false;
        private static IEnumerator OnbeamUpPlayer(On.ShipTeleporter.orig_beamUpPlayer orig, ShipTeleporter self)
        {
            Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Ask to beam up player");
            Plugin.EnergySystem().AskToBeamUpPlayerServerRpc();
            while (!allowedToBeamUpPlayer)
            {
                yield return new WaitForSeconds(1 / 1000f);
            }
            Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Got permission to beam up");
            allowedToBeamUpPlayer = false;

            var enumerator = orig(self);
            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }

        public static void AllowToKeepSomeItems()
        {
            allowedToKeepSomeItems = true;
        }
        private static bool allowedToKeepSomeItems = false;
        private static void OnDropAllHeldItems(On.GameNetcodeStuff.PlayerControllerB.orig_DropAllHeldItems orig, PlayerControllerB self, bool itemsFall = true, bool disconnecting = false)
        {
            if (allowedToKeepSomeItems)
            {
                Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Dropping only some items");
                allowedToKeepSomeItems = false;
                int droppedSlot1 = UnityEngine.Random.RandomRangeInt(0, self.ItemSlots.Length);
                int droppedSlot2 = (droppedSlot1 + UnityEngine.Random.RandomRangeInt(1, self.ItemSlots.Length)) % self.ItemSlots.Length;
                float droppedWeight = 0;
                for (int i = 0; i < self.ItemSlots.Length; i++)
                {
                    if (!(i == droppedSlot1 || i == droppedSlot2)) { continue; }
                    
                    GrabbableObject grabbableObject = self.ItemSlots[i];
                    if (!(grabbableObject != null))
                    {
                        continue;
                    }
                    droppedWeight += grabbableObject.itemProperties.weight - 1f;
                    if (itemsFall)
                    {
                        grabbableObject.parentObject = null;
                        grabbableObject.heldByPlayerOnServer = false;
                        if (self.isInElevator)
                        {
                            grabbableObject.transform.SetParent(self.playersManager.elevatorTransform, worldPositionStays: true);
                        }
                        else
                        {
                            grabbableObject.transform.SetParent(self.playersManager.propsContainer, worldPositionStays: true);
                        }
                        self.SetItemInElevator(self.isInHangarShipRoom, self.isInElevator, grabbableObject);
                        grabbableObject.EnablePhysics(enable: true);
                        grabbableObject.EnableItemMeshes(enable: true);
                        grabbableObject.transform.localScale = grabbableObject.originalScale;
                        grabbableObject.isHeld = false;
                        grabbableObject.isPocketed = false;
                        grabbableObject.startFallingPosition = grabbableObject.transform.parent.InverseTransformPoint(grabbableObject.transform.position);
                        grabbableObject.FallToGround(randomizePosition: true);
                        grabbableObject.fallTime = UnityEngine.Random.Range(-0.3f, 0.05f);
                        if (self.IsOwner)
                        {
                            grabbableObject.DiscardItemOnClient();
                        }
                        else if (!grabbableObject.itemProperties.syncDiscardFunction)
                        {
                            grabbableObject.playerHeldBy = null;
                        }
                    }
                    if (self.IsOwner && !disconnecting)
                    {
                        ((Behaviour)(object)HUDManager.Instance.holdingTwoHandedItem).enabled = false;
                        ((Behaviour)(object)HUDManager.Instance.itemSlotIcons[i]).enabled = false;
                        HUDManager.Instance.ClearControlTips();
                        self.activatingItem = false;
                    }
                    self.ItemSlots[i] = null;
                }
                if (self.isHoldingObject && (self.currentItemSlot == droppedSlot1 || self.currentItemSlot == droppedSlot2))
                {
                    self.isHoldingObject = false;
                    if (self.currentlyHeldObjectServer != null)
                    {
                        self.SetSpecialGrabAnimationBool(setTrue: false, self.currentlyHeldObjectServer);
                    }
                    self.playerBodyAnimator.SetBool("cancelHolding", true);
                    self.playerBodyAnimator.SetTrigger("Throw");

                    self.activatingItem = false;
                    self.twoHanded = false;
                    
                    self.currentlyHeldObjectServer = null;
                }
                self.carryWeight = Mathf.Clamp(self.carryWeight - (droppedWeight), 0f, 10f);
            } else
            {
                Plugin.logger.LogInfo($"[{(NetworkManager.Singleton.IsServer ? "Server" : "Client")}]: Dropping all items");
                orig(self, itemsFall, disconnecting);
            }
        }

        public static void ScaleMap(float scale)
        {
            TerminalAccessibleObject[] array = [.. Object.FindObjectsOfType<TerminalAccessibleObject>()];
            foreach (TerminalAccessibleObject obj in array)
            {
                RectTransform trans = obj.mapRadarText.GetComponentInParent<RectTransform>();
                trans.transform.localScale *= scale;
            }
            StartOfRound.Instance.mapScreen.mapCamera.orthographicSize *= scale;
        }

        public static void ClearWeather()
        {
            StartOfRound.Instance.currentLevel.currentWeather = LevelWeatherType.None;
            RoundManager.Instance.SetToCurrentLevelWeather();
            StartOfRound.Instance.SetMapScreenInfoToCurrentLevel();
        }



        public static void SpawnTargetedBlasts()
        {
            // Revert default energy usage
            Plugin.EnergySystem().AddShipEnergy(TARGETED_BLAST.energyUsage);
            GrabbableObject[] targetBeacons = Object.FindObjectsOfType<GrabbableObject>().Where((obj) =>!obj.isHeld
                && obj.itemProperties.Equals(Plugin.TargetBeaconItem)
            ).ToArray();
            
            List<GrabbableObject> unvisitedBeacons = [.. targetBeacons];
            for (int i = 0; i < unvisitedBeacons.Count; i++)
            {
                GrabbableObject beacon = unvisitedBeacons[i];
                List<GrabbableObject> nearBeacons = new List<GrabbableObject>();
                foreach (GrabbableObject bcon in targetBeacons)
                {
                    if (bcon == beacon) { continue; }
                    Plugin.logger.LogInfo("Distance: " + Vector3.Distance(bcon.transform.position, beacon.transform.position));
                    if (Vector3.Distance(bcon.transform.position, beacon.transform.position) < MAX_BEACON_DISTANCE)
                    {
                        nearBeacons.Add(bcon);
                    }
                    if (nearBeacons.Count == 2) { break; }
                }
                if (nearBeacons.Count == 2)
                {
                    unvisitedBeacons.Remove(beacon);
                    unvisitedBeacons.RemoveAll(nearBeacons.Contains);
                    Vector3 explosionPos = beacon.transform.position + nearBeacons[0].transform.position + nearBeacons[1].transform.position;
                    explosionPos.Scale(new Vector3(1f / 3f, 1f / 3f, 1f / 3f));
                    Plugin.EnergySystem().AddShipEnergy(-TARGETED_BLAST.energyUsage);
                    Plugin.EnergySystem().SpawnExplosionClientRpc(explosionPos + Vector3.up);
                }
            }
        }

        public static void SpawnBlastExplosion(Vector3 pos)
        {
            Plugin.EnergySystem().StartCoroutine(ScheduleExplosion(pos));
        }

        private static IEnumerator ScheduleExplosion(Vector3 pos)
        {
            GrabbableObject[] targetBeacons = Object.FindObjectsOfType<GrabbableObject>().Where((obj) => 
                !obj.isHeld && Vector3.Distance(obj.transform.position,pos) < MAX_BEACON_DISTANCE
                && obj.itemProperties.Equals(Plugin.TargetBeaconItem)
           ).ToArray();

            foreach (GrabbableObject obj in targetBeacons)
            {
                obj.GetComponent<AudioSource>().PlayOneShot(Plugin.BlastBuildup);
            }

            yield return new WaitForSeconds(1.75f);

            Landmine.SpawnExplosion(pos, true, 5.7f, 6f, 30, 35f);
            Collider[] colliders = Physics.OverlapSphere(pos, 5.7f, 524288, QueryTriggerInteraction.Collide);
            foreach (Collider collider in colliders)
            {
                EnemyAICollisionDetect componentInChildren2 = collider.gameObject.GetComponentInChildren<EnemyAICollisionDetect>();
                if ((Object)(object)componentInChildren2 != null && componentInChildren2.mainScript.IsOwner)
                {
                    bool canDie = componentInChildren2.mainScript.enemyType.canDie;
                    componentInChildren2.mainScript.enemyType.canDie = true;
                    componentInChildren2.mainScript.KillEnemyOnOwnerClient(!canDie);
                    componentInChildren2.mainScript.enemyType.canDie = canDie;
                }
            }

            yield break;
        }
    }
}
