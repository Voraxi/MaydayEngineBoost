using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Mayday;
using Mayday.Flight;
using Mayday.UI;
using Mirror;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MaydayEngineBoost
{
    [BepInPlugin(PluginId, "Mayday Engine Boost", "1.4.0")]
    public sealed class EngineBoostPlugin : BasePlugin
    {
        private const string PluginId = "local.mayday.engineboost";
        internal const float MinimumThrust = 0.10f;
        internal const float MaximumThrust = 6.00f;
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
            MethodInfo flightStep = AccessTools.DeclaredMethod(typeof(FlightSim), "Step",
                new[] { typeof(float), typeof(FlightSim.Controls).MakeByRefType() });

            if (engineThrust == null || flightStep == null)
            {
                Log.LogWarning("Engine boost disabled: this game build has a different thrust method.");
                return;
            }

            MethodInfo postfix = AccessTools.DeclaredMethod(
                typeof(EngineBoostPlugin), nameof(ScaleEngineThrust));
            MethodInfo prefix = AccessTools.DeclaredMethod(
                typeof(EngineBoostPlugin), nameof(ApplyThrustAboveCap));
            var harmony = new Harmony(PluginId);
            try
            {
                harmony.Patch(engineThrust, postfix: new HarmonyMethod(postfix));
                harmony.Patch(flightStep, prefix: new HarmonyMethod(prefix));
                AddComponent<EngineBoostPanel>();
            }
            catch
            {
                harmony.UnpatchSelf();
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

        private static void ApplyThrustAboveCap(FlightSim __instance, float __0, ref FlightSim.Controls __1)
        {
            if (ThrustMultiplier.Value <= 1f || !NetworkServer.active || __1.reverse)
                return;

            // FlightSim.Step clamps each engine to 1 before converting thrust to acceleration.
            // Add only the overflow so Step still handles normal thrust, drag and position.
            float overflow = Mathf.Max(0f, __1.thrustL - 1f) + Mathf.Max(0f, __1.thrustR - 1f);
            if (overflow <= 0f ||
                (__instance.OnGround && (__1.parkingBrake || __1.brakes > 0.01f || __instance.Backward)))
                return;

            const float AccelerationPerEngine = 0.95f; // FlightSim.Step, demo build 26546546.
            __instance.Speed += overflow * AccelerationPerEngine * __0;
        }
    }

    public sealed class EngineBoostPanel : MonoBehaviour
    {
        private const float Width = 444f;
        private const float Height = 276f;
        private const float Margin = 24f;
        private const float Step = 0.05f;

        private static readonly Color Surface = new Color32(18, 24, 31, 255);
        private static readonly Color Border = new Color32(53, 64, 75, 255);
        private static readonly Color Control = new Color32(35, 44, 53, 255);
        private static readonly Color Hover = new Color32(51, 65, 76, 255);
        private static readonly Color Text = new Color32(239, 245, 249, 255);
        private static readonly Color Muted = new Color32(160, 175, 189, 255);
        private static readonly Color Teal = new Color32(112, 228, 209, 255);
        private static readonly Color Stock = new Color32(244, 193, 109, 255);

        private static readonly Rect CloseButton = new Rect(382f, 12f, 42f, 36f);
        private static readonly Rect MinusButton = new Rect(24f, 220f, 46f, 36f);
        private static readonly Rect PlusButton = new Rect(78f, 220f, 46f, 36f);
        private static readonly Rect Slider = new Rect(30f, 134f, 384f, 44f);
        private static readonly Rect ResetButton = new Rect(267f, 220f, 153f, 36f);

        private bool open;
        private bool dragging;
        private bool settingsDirty;
        private RectTransform canvasRoot;
        private RectTransform panel;
        private RectTransform controls;
        private RectTransform guest;
        private Image closeButton;
        private Image minusButton;
        private Image plusButton;
        private Image resetButton;
        private Image progress;
        private Image thumb;
        private Image thumbCenter;
        private UIText number;
        private UIText status;
        private UIText session;
        private float displayedValue = float.NaN;
        private bool displayedServer;

        public EngineBoostPanel(IntPtr pointer) : base(pointer) { }

        private static bool CanAdjust => NetworkServer.active || !NetworkClient.active;

        private static float PanelScale
        {
            get
            {
                float preferred = Mathf.Clamp(Screen.height / 1080f, 0.88f, 1.75f);
                return Mathf.Min(preferred, Mathf.Min(
                    (Screen.width - 2f * Margin) / Width,
                    (Screen.height - 2f * Margin) / Height));
            }
        }

        private static Vector2 PanelPointer(Mouse mouse, float scale)
        {
            Vector2 position = mouse.position.ReadValue();
            float left = Screen.width - Margin - Width * scale;
            return new Vector2(position.x - left, Screen.height - position.y - Margin) / scale;
        }

        private void Update()
        {
            if (open && !UIState.IsOpen(this))
            {
                Hide();
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
            Vector2 pointer = mouse != null && scale > 0f
                ? PanelPointer(mouse, scale) : new Vector2(-1f, -1f);
            if (mouse != null && HandleMouse(mouse, pointer, canAdjust))
                return;

            RefreshView(scale, pointer, canAdjust);
        }

        private bool HandleMouse(Mouse mouse, Vector2 pointer, bool canAdjust)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (CloseButton.Contains(pointer))
                {
                    Close();
                    return true;
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

            if (dragging && canAdjust && mouse.leftButton.isPressed)
            {
                float ratio = Mathf.InverseLerp(Slider.xMin, Slider.xMax, pointer.x);
                SetMultiplier(Mathf.Lerp(EngineBoostPlugin.MinimumThrust, EngineBoostPlugin.MaximumThrust, ratio));
            }
            else if (dragging)
            {
                dragging = false;
                SaveSetting();
            }

            return false;
        }

        private void Open()
        {
            if (canvasRoot == null)
            {
                try
                {
                    BuildView();
                }
                catch
                {
                    if (canvasRoot != null)
                        Destroy(canvasRoot.gameObject);
                    canvasRoot = null;
                    throw;
                }
            }

            RefreshView(PanelScale, new Vector2(-1f, -1f), CanAdjust);
            UIState.Push(this);
            canvasRoot.gameObject.SetActive(true);
            open = true;
        }

        private void Close()
        {
            if (open)
            {
                Hide();
                UIState.Pop(this);
            }
            SaveSetting();
        }

        private void Hide()
        {
            open = false;
            dragging = false;
            if (canvasRoot != null)
                canvasRoot.gameObject.SetActive(false);
        }

        private void OnDisable() => Close();

        private void BuildView()
        {
            canvasRoot = UIF.Rect("Engine Boost Canvas", transform);
            canvasRoot.gameObject.SetActive(false);
            Canvas canvas = canvasRoot.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 28000;

            panel = UIF.Rect("Thrust Panel", canvasRoot);
            Vector2 topRight = new Vector2(1f, 1f);
            UIF.At(panel, topRight, topRight, new Vector2(-Margin, -Margin), new Vector2(Width, Height));

            UIF.ShadowFor(panel, 22f, 16f, -8f, 0.42f);
            UIF.Stretch(UIF.Round(panel, "Panel Border", Border, 21f).rectTransform);
            UIF.Stretch(UIF.Round(panel, "Panel Surface", Surface, 20f).rectTransform, 1f, 1f, 1f, 1f);
            TextAt(panel, "Engine thrust", new Rect(24f, 14f, 320f, 35f), 24f, Text, bold: true);
            closeButton = AddButton(panel, CloseButton, "Close", 13f);
            Shape(panel, "Header Divider", new Rect(24f, 58f, 396f, 1f), Border, 0f);

            controls = UIF.Rect("Host Controls", panel);
            UIF.Stretch(controls);
            number = TextAt(controls, "", new Rect(24f, 67f, 197f, 60f), 48f, Text, bold: true);
            status = TextAt(controls, "", new Rect(222f, 76f, 198f, 24f), 15f, Text, TextAlignmentOptions.MidlineRight, true);
            session = TextAt(controls, "", new Rect(222f, 101f, 198f, 22f), 13f, Muted, TextAlignmentOptions.MidlineRight);

            float center = Slider.center.y;
            float stockX = Slider.x + Slider.width * SliderPosition(1f);
            Shape(controls, "Slider Track", new Rect(Slider.x, center - 4f, Slider.width, 8f), Border, 4f);
            progress = Shape(controls, "Slider Fill", new Rect(Slider.x, center - 4f, 1f, 8f), Teal, 4f);
            Shape(controls, "Stock Marker", new Rect(stockX - 1.5f, center - 8f, 3f, 16f), Stock, 1.5f);
            thumb = Shape(controls, "Slider Thumb", new Rect(Slider.x - 12f, center - 12f, 24f, 24f), Teal, 12f);
            thumbCenter = Shape(thumb.rectTransform, "Slider Thumb Center", new Rect(3f, 3f, 18f, 18f), Surface, 9f);
            TextAt(controls, $"{EngineBoostPlugin.MinimumThrust * 100f:0}%", new Rect(23f, 174f, 43f, 26f), 13f, Muted);
            TextAt(controls, "100% stock", new Rect(stockX - 49f, 174f, 98f, 26f), 13f, Stock, TextAlignmentOptions.Center);
            TextAt(controls, $"{EngineBoostPlugin.MaximumThrust * 100f:0}%", new Rect(361f, 174f, 61f, 26f), 13f, Muted, TextAlignmentOptions.MidlineRight);

            Shape(controls, "Footer Divider", new Rect(24f, 206f, 396f, 1f), Border, 0f);
            minusButton = AddButton(controls, MinusButton, "−", 22f);
            plusButton = AddButton(controls, PlusButton, "+", 22f);
            resetButton = AddButton(controls, ResetButton, "Reset to stock", 14f);

            guest = UIF.Rect("Guest Message", panel);
            UIF.Stretch(guest);
            TextAt(guest, "Host controls thrust", new Rect(24f, 94f, 396f, 42f), 22f, Text, bold: true);
            TextAt(guest, "You can adjust thrust when you host a lobby.", new Rect(24f, 144f, 396f, 37f), 15f, Muted);
        }

        private void RefreshView(float scale, Vector2 pointer, bool canAdjust)
        {
            if (panel.localScale.x != scale)
                panel.localScale = new Vector3(scale, scale, 1f);

            if (controls.gameObject.activeSelf != canAdjust)
                controls.gameObject.SetActive(canAdjust);
            if (guest.gameObject.activeSelf == canAdjust)
                guest.gameObject.SetActive(!canAdjust);

            closeButton.color = CloseButton.Contains(pointer) ? Hover : Control;
            if (!canAdjust)
                return;

            float current = EngineBoostPlugin.ThrustMultiplier.Value;
            bool stock = Mathf.Approximately(current, 1f);
            bool server = NetworkServer.active;
            if (current != displayedValue || server != displayedServer)
            {
                displayedValue = current;
                displayedServer = server;
                number.Set($"{current * 100f:0}%");
                status.Set(stock ? "Stock thrust" : $"{(current - 1f) * 100f:+0;-0;0}% vs stock");
                status.Color = stock ? Stock : Teal;
                session.Set(server ? "Host control" : "Applies when you host");
                float width = Slider.width * SliderPosition(current);
                Rect size = new Rect(Slider.x, Slider.center.y - 4f, width, 8f);
                Place(progress.rectTransform, size);
                progress.enabled = width > 0f;
                Place(thumb.rectTransform, new Rect(Slider.x + width - 12f, Slider.center.y - 12f, 24f, 24f));
            }

            thumbCenter.color = dragging || Slider.Contains(pointer) ? Hover : Surface;
            minusButton.color = MinusButton.Contains(pointer) && current > EngineBoostPlugin.MinimumThrust ? Hover : Control;
            plusButton.color = PlusButton.Contains(pointer) && current < EngineBoostPlugin.MaximumThrust ? Hover : Control;
            resetButton.color = ResetButton.Contains(pointer) && !stock ? Hover : Control;
        }

        private static float SliderPosition(float value) =>
            Mathf.InverseLerp(EngineBoostPlugin.MinimumThrust, EngineBoostPlugin.MaximumThrust, value);

        private static Image AddButton(RectTransform parent, Rect rect, string text, float fontSize)
        {
            Image fill = Shape(parent, text, rect, Control, 9f);
            UIF.Stretch(UIF.Text(fill.rectTransform, "Label", text, fontSize, Text, TextAlignmentOptions.Center).Rect);
            return fill;
        }

        private static Image Shape(RectTransform parent, string name, Rect rect, Color color, float radius)
        {
            Image image = radius > 0f
                ? UIF.Round(parent, name, color, radius)
                : UIF.Image(parent, name, null, color);
            Place(image.rectTransform, rect);
            return image;
        }

        private static UIText TextAt(RectTransform parent, string text, Rect rect, float size,
            Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
        {
            UIText label = UIF.Text(parent, "Text", text, size, color, align);
            if (bold)
                label.Tmp.fontStyle = FontStyles.Bold;
            Place(label.Rect, rect);
            return label;
        }

        private static void Place(RectTransform element, Rect rect)
        {
            Vector2 topLeft = new Vector2(0f, 1f);
            UIF.At(element, topLeft, topLeft, new Vector2(rect.x, -rect.y), new Vector2(rect.width, rect.height));
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
