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
    [BepInPlugin(PluginId, "Mayday Engine Boost", "1.3.0")]
    public sealed class EngineBoostPlugin : BasePlugin
    {
        private const string PluginId = "local.mayday.engineboost";
        internal const float MinimumThrust = 0.10f;
        internal const float MaximumThrust = 5.00f;
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
        private const float Width = 420f;
        private const float Height = 252f;
        private const float Margin = 16f;
        private const float Step = 0.05f;

        private static readonly Color Surface = new Color(0.047f, 0.071f, 0.102f, 0.98f);
        private static readonly Color Raised = new Color(0.078f, 0.110f, 0.153f);
        private static readonly Color Border = new Color(0.16f, 0.22f, 0.29f);
        private static readonly Color Text = new Color(0.91f, 0.95f, 0.98f);
        private static readonly Color Muted = new Color(0.64f, 0.71f, 0.79f);
        private static readonly Color Cyan = new Color(0.37f, 0.85f, 0.94f);
        private static readonly Color Amber = new Color(0.99f, 0.76f, 0.40f);

        private static readonly Rect CloseButton = new Rect(364f, 13f, 36f, 32f);
        private static readonly Rect MinusButton = new Rect(20f, 124f, 36f, 36f);
        private static readonly Rect PlusButton = new Rect(364f, 124f, 36f, 36f);
        private static readonly Rect Slider = new Rect(70f, 124f, 280f, 36f);
        private static readonly Rect ResetButton = new Rect(20f, 202f, 134f, 32f);

        private bool open;
        private bool dragging;
        private bool settingsDirty;
        private GUIStyle title;
        private GUIStyle number;
        private GUIStyle label;
        private GUIStyle right;
        private GUIStyle centered;
        private GUIStyle button;

        public EngineBoostPanel(IntPtr pointer) : base(pointer) { }

        private static bool CanAdjust => NetworkServer.active || !NetworkClient.active;

        private static float PanelScale
        {
            get
            {
                float preferred = Mathf.Clamp(Screen.height / 1080f, 0.9f, 1.6f);
                return Mathf.Min(preferred, Mathf.Min(
                    (Screen.width - 2f * Margin) / Width,
                    (Screen.height - 2f * Margin) / Height));
            }
        }

        private static Vector2 PanelOrigin(float scale) =>
            new Vector2(Screen.width - Margin - Width * scale, Margin);

        private static Vector2 PanelPointer(Mouse mouse, float scale)
        {
            Vector2 position = mouse.position.ReadValue();
            return (new Vector2(position.x, Screen.height - position.y) - PanelOrigin(scale)) / scale;
        }

        private void Update()
        {
            if (open && !UIState.IsOpen(this))
            {
                open = false;
                dragging = false;
                SaveSetting();
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f7Key.wasPressedThisFrame)
            {
                if (open)
                    Close();
                else
                    Open();
                return;
            }

            if (!open)
                return;

            bool canAdjust = CanAdjust;
            if (canAdjust && keyboard != null)
            {
                if (keyboard.homeKey.wasPressedThisFrame)
                    ApplyAndSave(1f);
                else if (keyboard.leftArrowKey.wasPressedThisFrame)
                    ApplyAndSave(EngineBoostPlugin.ThrustMultiplier.Value - Step);
                else if (keyboard.rightArrowKey.wasPressedThisFrame)
                    ApplyAndSave(EngineBoostPlugin.ThrustMultiplier.Value + Step);
            }

            Mouse mouse = Mouse.current;
            float scale = PanelScale;
            if (mouse == null || scale <= 0f)
                return;

            Vector2 pointer = PanelPointer(mouse, scale);
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (CloseButton.Contains(pointer))
                {
                    Close();
                    return;
                }
                if (canAdjust)
                {
                    float current = EngineBoostPlugin.ThrustMultiplier.Value;
                    if (ResetButton.Contains(pointer))
                        ApplyAndSave(1f);
                    else if (MinusButton.Contains(pointer))
                        ApplyAndSave(current - Step);
                    else if (PlusButton.Contains(pointer))
                        ApplyAndSave(current + Step);

                    dragging = Slider.Contains(pointer);
                }
            }

            if (!dragging)
                return;

            if (canAdjust && mouse.leftButton.isPressed)
            {
                float ratio = Mathf.InverseLerp(Slider.xMin, Slider.xMax, pointer.x);
                SetMultiplier(Mathf.Lerp(EngineBoostPlugin.MinimumThrust, EngineBoostPlugin.MaximumThrust, ratio));
            }
            else
            {
                dragging = false;
                SaveSetting();
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

            float scale = PanelScale;
            if (scale <= 0f)
                return;

            if (title == null)
            {
                title = MakeStyle(21, TextAnchor.MiddleLeft);
                number = MakeStyle(42, TextAnchor.MiddleLeft);
                label = MakeStyle(13, TextAnchor.MiddleLeft);
                right = MakeStyle(13, TextAnchor.MiddleRight);
                centered = MakeStyle(13, TextAnchor.MiddleCenter);
                button = MakeStyle(14, TextAnchor.MiddleCenter);
            }

            Mouse mouse = Mouse.current;
            Vector2 pointer = mouse != null ? PanelPointer(mouse, scale) : new Vector2(-1f, -1f);
            Vector2 origin = PanelOrigin(scale);
            int previousDepth = GUI.depth;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.depth = -100;
            GUI.matrix = previousMatrix * Matrix4x4.TRS(
                new Vector3(origin.x, origin.y, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));
            try
            {
                DrawPanel(pointer);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.depth = previousDepth;
            }
        }

        private void DrawPanel(Vector2 pointer)
        {
            Fill(new Rect(4f, 5f, Width, Height), new Color(0f, 0f, 0f, 0.36f));
            Frame(new Rect(0f, 0f, Width, Height), Surface);
            Fill(new Rect(0f, 0f, 3f, 56f), Cyan);
            Fill(new Rect(20f, 55f, 380f, 1f), Border);
            Write(new Rect(20f, 11f, 300f, 36f), "Engine thrust", title, Text);
            DrawButton(CloseButton, "×", pointer, true);

            if (CanAdjust)
                DrawControls(pointer);
            else
                DrawGuestState();
        }

        private void DrawControls(Vector2 pointer)
        {
            float current = EngineBoostPlugin.ThrustMultiplier.Value;
            bool stock = Mathf.Approximately(current, 1f);
            Write(new Rect(20f, 61f, 146f, 56f), $"{current * 100f:0}%", number, Cyan);
            string status = stock ? "Stock thrust" : $"{(current - 1f) * 100f:+0;-0;0}% vs stock";
            Write(new Rect(170f, 70f, 230f, 20f), status, right, stock ? Amber : Text);
            Write(new Rect(170f, 91f, 230f, 20f),
                NetworkServer.active ? "Host control" : "Applies when you host", right, Muted);

            DrawButton(MinusButton, "−", pointer, current > EngineBoostPlugin.MinimumThrust);
            DrawButton(PlusButton, "+", pointer, current < EngineBoostPlugin.MaximumThrust);
            DrawSlider(current, Slider.Contains(pointer) || dragging);
            DrawButton(ResetButton, "Reset to stock", pointer, !stock);
            Write(new Rect(163f, 202f, 237f, 32f), "← / →  Adjust    Home  Reset    F7  Close", right, Muted);
        }

        private void DrawSlider(float current, bool highlighted)
        {
            float stockX = Slider.x + Slider.width * SliderPosition(1f);
            float valueX = Slider.x + Slider.width * SliderPosition(current);
            float midY = Slider.center.y;

            Fill(new Rect(Slider.x, midY - 3f, Slider.width, 6f), Border);
            Fill(new Rect(Mathf.Min(valueX, stockX), midY - 3f, Mathf.Abs(valueX - stockX), 6f), Cyan);
            Fill(new Rect(stockX - 1f, midY - 12f, 2f, 24f), Amber);
            Fill(new Rect(valueX - 6f, midY - 8f, 12f, 16f), highlighted ? Color.white : Cyan);

            Write(new Rect(Slider.x - 8f, 164f, 40f, 20f), $"{EngineBoostPlugin.MinimumThrust * 100f:0}%", label, Muted);
            Write(new Rect(stockX - 60f, 164f, 120f, 20f), "100% stock", centered, Amber);
            Write(new Rect(Slider.xMax - 62f, 164f, 62f, 20f), $"{EngineBoostPlugin.MaximumThrust * 100f:0}%", right, Muted);
        }

        private void DrawGuestState()
        {
            Frame(new Rect(20f, 77f, 380f, 105f), Raised);
            Write(new Rect(36f, 91f, 348f, 35f), "Host controls thrust", title, Text);
            Write(new Rect(36f, 128f, 348f, 24f), "The slider is available when you host a lobby.", label, Muted);
            Write(new Rect(220f, 202f, 180f, 32f), "F7  Close", right, Muted);
        }

        private static float SliderPosition(float value) =>
            Mathf.InverseLerp(EngineBoostPlugin.MinimumThrust, EngineBoostPlugin.MaximumThrust, value);

        private void DrawButton(Rect rect, string text, Vector2 pointer, bool enabled)
        {
            bool hover = enabled && rect.Contains(pointer);
            Frame(rect, hover ? new Color(0.15f, 0.24f, 0.31f) : Raised);
            Write(rect, text, button, hover ? Cyan : enabled ? Text : Muted);
        }

        private static GUIStyle MakeStyle(int size, TextAnchor alignment) =>
            new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                alignment = alignment,
            };

        private static void Write(Rect rect, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }

        private static void Frame(Rect rect, Color color)
        {
            Fill(rect, Border);
            Fill(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), color);
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void ApplyAndSave(float value)
        {
            SetMultiplier(value);
            SaveSetting();
        }

        private void SetMultiplier(float value)
        {
            float snapped = Mathf.Clamp(Mathf.Round(value / Step) / 20f,
                EngineBoostPlugin.MinimumThrust, EngineBoostPlugin.MaximumThrust);
            if (snapped == EngineBoostPlugin.ThrustMultiplier.Value)
                return;

            EngineBoostPlugin.ThrustMultiplier.Value = snapped;
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
