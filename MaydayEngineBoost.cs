using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Mayday;
using Mayday.Flight;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MaydayEngineBoost
{
    [BepInPlugin(PluginId, "Mayday Engine Boost", "1.1.0")]
    public sealed class EngineBoostPlugin : BasePlugin
    {
        private const string PluginId = "local.mayday.engineboost";
        internal const float MinimumThrust = 0.10f;
        internal const float MaximumThrust = 2.00f;
        internal static ConfigEntry<float> ThrustMultiplier { get; private set; }

        public override void Load()
        {
            ConfigEntry<bool> enabled = Config.Bind(
                "Engine", "Enabled", true,
                "Enable the plugin and its F7 controls. Restart after changing this setting.");
            ThrustMultiplier = Config.Bind(
                "Engine",
                "ThrustMultiplier",
                1.25f,
                new ConfigDescription(
                    "Forward engine thrust multiplier. 1.00 is stock and 1.25 adds 25% thrust. Press F7 to adjust it in game.",
                    new AcceptableValueRange<float>(MinimumThrust, MaximumThrust)));

            if (!enabled.Value)
            {
                Log.LogInfo("Engine boost disabled. Stock thrust is active.");
                return;
            }

            Type outputType = typeof(float).MakeByRefType();
            MethodInfo engineThrust = AccessTools.DeclaredMethod(
                typeof(PlaneFlight), "EngineThrust", new[] { outputType, outputType });

            if (engineThrust == null)
            {
                Log.LogWarning("Engine boost disabled: this game build has a different thrust method.");
                return;
            }

            MethodInfo postfix = AccessTools.DeclaredMethod(
                typeof(EngineBoostPlugin), nameof(ScaleEngineThrust));
            var harmony = new Harmony(PluginId);
            harmony.Patch(engineThrust, postfix: new HarmonyMethod(postfix));
            try
            {
                AddComponent<EngineBoostPanel>();
            }
            catch
            {
                harmony.Unpatch(engineThrust, postfix);
                throw;
            }
            Config.SaveOnConfigSet = false;
            Log.LogInfo($"Forward thrust multiplier set to {ThrustMultiplier.Value:0.00}x. Press F7 for controls.");
        }

        private static void ScaleEngineThrust(ref float __0, ref float __1)
        {
            float multiplier = ThrustMultiplier.Value;
            if (multiplier == 1f || !NetworkServer.active)
                return;

            if (__0 > 0f)
                __0 *= multiplier;
            if (__1 > 0f)
                __1 *= multiplier;
        }
    }

    public sealed class EngineBoostPanel : MonoBehaviour
    {
        private const float PanelHeight = 164f;
        private const float Margin = 16f;
        private bool open;
        private bool dragging;
        private bool settingsDirty;
        private GUIStyle heading;
        private GUIStyle amount;
        private GUIStyle note;

        public EngineBoostPanel(IntPtr pointer) : base(pointer) { }

        private void Update()
        {
            if (open && !UIState.IsOpen(this))
            {
                open = false;
                SaveSetting();
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f7Key.wasPressedThisFrame)
            {
                if (open)
                    Close();
                else
                    Open();
            }

            Mouse mouse = Mouse.current;
            if (!open || mouse == null)
                return;

            Rect panel = PanelRect;
            Vector2 position = mouse.position.ReadValue();
            var pointer = new Vector2(position.x - panel.x, Screen.height - position.y - panel.y);
            Rect track = TrackRect(panel.width);
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (CloseRect(panel.width).Contains(pointer))
                {
                    Close();
                    return;
                }
                if (CanAdjust && StockRect.Contains(pointer))
                {
                    SetMultiplier(1f);
                    SaveSetting();
                }
                dragging = CanAdjust && track.Contains(pointer);
            }

            if (dragging && CanAdjust && mouse.leftButton.isPressed)
            {
                float value = Mathf.Lerp(EngineBoostPlugin.MinimumThrust, EngineBoostPlugin.MaximumThrust,
                    Mathf.InverseLerp(track.xMin, track.xMax, pointer.x));
                SetMultiplier(Mathf.Round(value * 20f) / 20f);
            }
            else if (dragging)
            {
                dragging = false;
                SaveSetting();
            }
        }

        private static bool CanAdjust => NetworkServer.active || !NetworkClient.active;
        private static Rect StockRect => new Rect(16f, 116f, 126f, 30f);
        private static Rect CloseRect(float width) => new Rect(width - 88f, 116f, 72f, 30f);
        private static Rect TrackRect(float width) => new Rect(18f, 52f, width - 36f, 28f);
        private static Rect PanelRect
        {
            get
            {
                float width = Mathf.Min(380f, Screen.width - 2f * Margin);
                return new Rect(Screen.width - width - Margin, Margin, width, PanelHeight);
            }
        }

        private void Open()
        {
            UIState.Push(this);
            open = true;
        }

        private void Close()
        {
            if (open)
            {
                open = false;
                dragging = false;
                UIState.Pop(this);
            }
            SaveSetting();
        }

        private void OnDisable() => Close();

        private void OnGUI()
        {
            if (!open)
                return;

            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                };
                heading.normal.textColor = Color.white;
                amount = new GUIStyle(heading) { alignment = TextAnchor.MiddleRight };
                amount.normal.textColor = new Color(0.50f, 0.85f, 1f);
                note = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                note.normal.textColor = new Color(0.73f, 0.80f, 0.86f);
            }

            Rect panel = PanelRect;
            float width = panel.width;
            int previousDepth = GUI.depth;
            GUI.depth = -100;
            Fill(panel, new Color(0.08f, 0.12f, 0.17f, 0.98f));
            Fill(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.37f, 0.75f, 0.96f));

            GUI.BeginGroup(panel);
            GUI.Label(new Rect(16f, 12f, width - 112f, 32f), "Engine thrust", heading);
            float current = EngineBoostPlugin.ThrustMultiplier.Value;
            bool canAdjust = CanAdjust;
            string value = canAdjust ? $"{current * 100f:0}%" : "Host only";
            GUI.Label(new Rect(width - 112f, 12f, 96f, 32f), value, amount);

            Rect track = TrackRect(width);
            Fill(new Rect(track.x, track.center.y - 2f, track.width, 4f), new Color(0.30f, 0.39f, 0.48f));
            if (canAdjust)
            {
                float position = Mathf.InverseLerp(EngineBoostPlugin.MinimumThrust, EngineBoostPlugin.MaximumThrust, current);
                float progress = track.width * position;
                var blue = new Color(0.37f, 0.75f, 0.96f);
                Fill(new Rect(track.x, track.center.y - 2f, progress, 4f), blue);
                Fill(new Rect(track.x + progress - 4f, track.center.y - 8f, 8f, 16f), blue);
                GUI.Box(StockRect, "Reset to stock", GUI.skin.button);
            }
            GUI.Label(new Rect(16f, 80f, width - 32f, 22f),
                canAdjust ? "10% minimum  ·  100% stock  ·  200% maximum" : "The lobby host controls engine thrust.", note);

            GUI.Box(CloseRect(width), "Close", GUI.skin.button);
            GUI.EndGroup();
            GUI.depth = previousDepth;
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void SetMultiplier(float value)
        {
            if (value == EngineBoostPlugin.ThrustMultiplier.Value)
                return;

            EngineBoostPlugin.ThrustMultiplier.Value = value;
            settingsDirty = true;
        }

        private void SaveSetting()
        {
            if (!settingsDirty)
                return;

            EngineBoostPlugin.ThrustMultiplier.ConfigFile.Save();
            settingsDirty = false;
        }
    }
}
