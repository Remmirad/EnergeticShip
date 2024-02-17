using UnityEngine;
using Object = UnityEngine.Object;

namespace EnergeticShip
{
    public class Actions
    {
        public static readonly Action SCALE_MAP = new(() =>
        {
            Plugin.EnergySystem().ScaleMapClientRpc(2f);
        }, () =>
        {
            Plugin.EnergySystem().ScaleMapClientRpc(0.5f);
        }, 3f, true);

        public static readonly Action DISABLE_WEATHER = new(() => { Plugin.EnergySystem().ClearWeatherClientRpc(); }, () => { }, 25f, false);

        public class Action
        {
            public delegate void Callback();

            public float energyUsage { get; }
            public float elapsedSeconds { get; set; }
            private bool active;
            
            private Callback startCB;
            private Callback stopCB;
            private bool needsEnergyConstantly;

            public Action(Callback startCB, Callback stopCB, float energyUsage, bool needsEnergyConstantly = false)
            {
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

            public void Start()
            {
                if (NeedsEnergyConstantly())
                {
                    active = true;
                    Plugin.EnergySystem().ScheduleAction(this);
                } else
                {
                    Plugin.EnergySystem().AddShipEnergy(-energyUsage);
                }
                startCB();
            }

            public void Stop() {
                if (NeedsEnergyConstantly())
                {
                    active = false;
                    Plugin.EnergySystem().UnscheduleAction(this);
                }
                if (stopCB != null) { stopCB(); }
            }

            public bool NeedsEnergyConstantly()
            {
                return needsEnergyConstantly;
            }

            public bool IsActive() { return active; }
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
    }
}
