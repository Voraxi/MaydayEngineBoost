using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mayday;
using Mayday.Flight;
using Mayday.Systems;
using Mayday.UI;
using Mirror;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MaydayEngineBoost
{
    [BepInPlugin(PluginId, "Mayday Engine Boost", "1.6.0")]
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
                    "Forward engine thrust multiplier. Extra thrust tapers near the airborne overspeed threshold. Press F7 to adjust it in game.",
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
            Type scalarRef = typeof(float).MakeByRefType();
            MethodInfo manualControl = AccessTools.DeclaredMethod(typeof(ControlLaw), "Manual", new[]
            {
                typeof(FlightSim), typeof(float), typeof(float), scalarRef, typeof(float), typeof(float),
                typeof(bool), typeof(float), typeof(float), scalarRef, scalarRef
            });

            if (engineThrust == null || flightStep == null || manualControl == null)
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
                harmony.Patch(manualControl,
                    prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(EngineBoostPlugin), nameof(TrimForBoostedSpeed))),
                    postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(EngineBoostPlugin), nameof(RestorePilotTrim))));
                InventoryExpansion.Install(harmony, Config);
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
            float speed = __instance.Speed;
            float extraSpeed = overflow * AccelerationPerEngine * __0;
            if (!__instance.OnGround)
            {
                // Stock autopilot commands a climb above 85 m/s. Manual overspeed protection begins above 88 m/s.
                const float TaperStart = 74f;
                const float BoostLimit = 80f;
                extraSpeed *= 1f - Mathf.InverseLerp(TaperStart, BoostLimit, speed);
                extraSpeed = Mathf.Min(extraSpeed, Mathf.Max(0f, BoostLimit - speed));
            }

            if (extraSpeed > 0f)
                __instance.Speed = speed + extraSpeed;
        }

        private static void TrimForBoostedSpeed(FlightSim __0, float __1, ref float __3, float __7, out float __state)
        {
            __state = float.NaN;
            float multiplier = ThrustMultiplier.Value;
            if (multiplier <= 1f || !NetworkServer.active || __0.OnGround ||
                __7 * multiplier <= 1f || Mathf.Abs(__1) > 0.05f || __0.Speed <= __3)
                return;

            // Neutral yoke normally climbs to shed speed above the last trim point.
            __state = __3;
            __3 = __0.Speed;
        }

        private static void RestorePilotTrim(ref float __3, float __state)
        {
            if (!float.IsNaN(__state))
                __3 = __state;
        }
    }

    internal static class InventoryExpansion
    {
        private const int StockSlots = 4;
        private const int ExpandedSlots = 6;
        private const float StockBarWidth = 420f;
        private const float SlotPitch = 108f;
        private static ConfigEntry<int> slotCount;

        internal static void Install(Harmony harmony, ConfigFile config)
        {
            slotCount = config.Bind("Inventory", "SlotCount", ExpandedSlots,
                new ConfigDescription("Hotbar slots for the local host. Restart the game after changing this setting.",
                    new AcceptableValueRange<int>(StockSlots, ExpandedSlots)));
            if (slotCount.Value == StockSlots)
                return;

            Patch(harmony, typeof(PlayerNet), nameof(PlayerNet.OnStartLocalPlayer), nameof(OnLocalPlayerReady));
            Patch(harmony, typeof(PlayerInteractor), "HandleHotbar", nameof(HandleExtraKeys));
            Patch(harmony, typeof(HotbarWidget), nameof(HotbarWidget.Build), nameof(PrepareHotbar), prefix: true);
            Patch(harmony, typeof(HotbarWidget), nameof(HotbarWidget.Tick), nameof(SizeHotbar));
        }

        private static void Patch(Harmony harmony, Type type, string method, string handler, bool prefix = false)
        {
            MethodInfo target = AccessTools.DeclaredMethod(type, method);
            if (target == null)
                throw new MissingMethodException(type.FullName, method);

            var patch = new HarmonyMethod(AccessTools.DeclaredMethod(typeof(InventoryExpansion), handler));
            harmony.Patch(target, prefix ? patch : null, prefix ? null : patch);
        }

        private static void OnLocalPlayerReady(PlayerNet __instance)
        {
            if (NetworkServer.activeHost && __instance.isServer)
                EnsureCapacity(__instance.Inventory);
        }

        internal static void EnsureCapacity(PlayerInventory inventory)
        {
            if (inventory == null || inventory.slots == null || inventory.slots.Count < StockSlots)
                return;

            while (inventory.slots.Count < slotCount.Value)
                inventory.slots.Add(0u);
        }

        private static void HandleExtraKeys(PlayerInteractor __instance)
        {
            PlayerNet player = __instance.player;
            if (!NetworkServer.activeHost || player == null || player != PlayerNet.Local || !player.isServer ||
                player.IsDead || player.IsSeated || UIState.BlocksGameplay)
                return;

            PlayerInventory inventory = player.Inventory;
            EnsureCapacity(inventory);
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.digit5Key.wasPressedThisFrame)
                SelectExtraSlot(inventory, 4);
            else if (keyboard.digit6Key.wasPressedThisFrame)
                SelectExtraSlot(inventory, 5);
        }

        internal static void SelectExtraSlot(PlayerInventory inventory, int index)
        {
            if (inventory != null && inventory.slots != null &&
                index >= StockSlots && index < Math.Min(slotCount.Value, inventory.slots.Count) && !inventory.HandsFull)
                inventory.Networkactive = index;
        }

        private static void PrepareHotbar(HotbarWidget __instance)
        {
            if (__instance.slots != null && __instance.slots.Length < slotCount.Value)
                __instance.slots = new Il2CppReferenceArray<HotbarWidget.Slot>(slotCount.Value);
        }

        private static void SizeHotbar(HotbarWidget __instance)
        {
            if (__instance.bar == null || __instance.slots == null || __instance.slots.Length <= StockSlots)
                return;

            PlayerNet player = PlayerNet.Local;
            PlayerInventory inventory = player != null ? player.Inventory : null;
            int shown = inventory != null && inventory.slots != null
                ? Mathf.Clamp(inventory.slots.Count, StockSlots, __instance.slots.Length)
                : StockSlots;
            for (int index = StockSlots; index < __instance.slots.Length; index++)
            {
                HotbarWidget.Slot slot = __instance.slots[index];
                if (slot != null && slot.rt != null && slot.rt.gameObject.activeSelf != (index < shown))
                    slot.rt.gameObject.SetActive(index < shown);
            }

            Vector2 size = __instance.bar.sizeDelta;
            float width = StockBarWidth + SlotPitch * (shown - StockSlots);
            if (size.x != width)
                __instance.bar.sizeDelta = new Vector2(width, size.y);
        }
    }

    internal static class FuelEditor
    {
        internal static FuelSystem Active
        {
            get
            {
                if (!NetworkServer.active)
                    return null;

                FuelSystem fuel = SysRefs.Fuel;
                return fuel != null && fuel.isServer && fuel.serverReady &&
                    fuel.tank != null && fuel.tank.Length >= 2 &&
                    fuel.Capacity > 0f && FuelSystem.ReserveCapacity > 0f ? fuel : null;
            }
        }

        internal static bool Apply(FuelSystem fuel, float? main, float? reserve)
        {
            if (fuel == null || fuel != Active ||
                (main.HasValue && !float.IsFinite(main.Value)) ||
                (reserve.HasValue && !float.IsFinite(reserve.Value)))
                return false;

            // Match the game's fuel command: update the simulation tanks and the woven SyncVars.
            if (main.HasValue)
            {
                float perTank = Mathf.Clamp01(main.Value) * fuel.Capacity;
                var tanks = fuel.tank;
                tanks[0] = perTank;
                tanks[1] = perTank;
                float networkLevel = Sys.Q(perTank, 0.05f);
                fuel.NetworkfuelL = networkLevel;
                fuel.NetworkfuelR = networkLevel;
            }

            if (reserve.HasValue)
            {
                float level = Mathf.Clamp01(reserve.Value) * FuelSystem.ReserveCapacity;
                fuel.reserveTank = level;
                fuel.NetworkreserveSync = Sys.Q(level, 0.05f);
            }

            return true;
        }

    }

    public sealed class EngineBoostPanel : MonoBehaviour
    {
        private const float Width = 444f;
        private const float Height = 480f;
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
        private FuelControls fuelControls;
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
            bool pressed = mouse.leftButton.wasPressedThisFrame;
            if (pressed)
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

            if (canAdjust)
                fuelControls.HandlePointer(pointer, pressed, mouse.leftButton.isPressed);

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
            fuelControls?.Clear();
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
            TextAt(panel, "Engine controls", new Rect(24f, 14f, 320f, 35f), 24f, Text, bold: true);
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
            fuelControls = new FuelControls(controls);

            guest = UIF.Rect("Guest Message", panel);
            UIF.Stretch(guest);
            TextAt(guest, "Host controls thrust and fuel", new Rect(24f, 94f, 396f, 42f), 22f, Text, bold: true);
            TextAt(guest, "You can adjust these when you host a lobby.", new Rect(24f, 144f, 396f, 37f), 15f, Muted);
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
            fuelControls.Refresh(pointer, canAdjust ? FuelEditor.Active : null);
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

        private sealed class FuelControls
        {
            private static readonly Rect ApplyRect = new Rect(267f, 427f, 153f, 36f);
            private readonly FuelSlider main;
            private readonly FuelSlider reserve;
            private readonly UIText message;
            private readonly RectTransform editor;
            private readonly Image apply;
            private FuelSystem fuel;
            private FuelSlider dragged;

            internal FuelControls(RectTransform parent)
            {
                Shape(parent, "Fuel Divider", new Rect(24f, 270f, 396f, 1f), Border, 0f);
                TextAt(parent, "Fuel", new Rect(24f, 279f, 396f, 29f), 19f, Text, bold: true);
                message = TextAt(parent, "Fuel controls appear when you host a loaded flight.",
                    new Rect(24f, 319f, 396f, 31f), 14f, Muted);
                editor = UIF.Rect("Fuel Controls", parent);
                UIF.Stretch(editor);
                main = new FuelSlider(editor, "Main tanks", 310f);
                reserve = new FuelSlider(editor, "Reserve", 366f);
                apply = AddButton(editor, ApplyRect, "Apply fuel", 14f);
            }

            internal void HandlePointer(Vector2 position, bool pressed, bool held)
            {
                if (fuel == null || fuel != FuelEditor.Active)
                    return;

                if (pressed)
                {
                    if (ApplyRect.Contains(position) && (main.Pending.HasValue || reserve.Pending.HasValue))
                    {
                        if (FuelEditor.Apply(fuel, main.Pending, reserve.Pending))
                            Clear();
                        return;
                    }

                    dragged = main.Track.Contains(position) ? main : reserve.Track.Contains(position) ? reserve : null;
                }

                if (dragged != null && (pressed || held))
                {
                    Rect track = dragged.Track;
                    dragged.Pending = Mathf.Round(100f * Mathf.InverseLerp(track.xMin, track.xMax, position.x)) / 100f;
                }
                else
                {
                    dragged = null;
                }
            }

            internal void Refresh(Vector2 position, FuelSystem activeFuel)
            {
                if (activeFuel != fuel)
                {
                    Clear();
                    fuel = activeFuel;
                }

                bool ready = fuel != null;
                if (editor.gameObject.activeSelf != ready)
                    editor.gameObject.SetActive(ready);
                if (message.gameObject.activeSelf == ready)
                    message.gameObject.SetActive(!ready);
                if (!ready)
                    return;

                main.Draw(fuel.Total, 2f * fuel.Capacity, main == dragged || main.Track.Contains(position));
                reserve.Draw(fuel.Reserve, FuelSystem.ReserveCapacity, reserve == dragged || reserve.Track.Contains(position));
                bool changed = main.Pending.HasValue || reserve.Pending.HasValue;
                apply.color = changed ? (ApplyRect.Contains(position) ? Hover : Control) : Surface;
            }

            internal void Clear()
            {
                main.Pending = null;
                reserve.Pending = null;
                dragged = null;
            }
        }

        private sealed class FuelSlider
        {
            internal readonly Rect Track;
            internal float? Pending;
            private readonly UIText amount;
            private readonly Image fill;
            private readonly Image thumb;
            private string displayed;
            private float previousFraction = -1f;

            internal FuelSlider(RectTransform parent, string label, float top)
            {
                Track = new Rect(30f, top + 23f, 384f, 26f);
                TextAt(parent, label, new Rect(24f, top, 145f, 24f), 14f, Text);
                amount = TextAt(parent, "", new Rect(177f, top, 243f, 24f), 14f, Muted, TextAlignmentOptions.MidlineRight);
                float y = Track.center.y - 3f;
                Shape(parent, label + " Track", new Rect(Track.x, y, Track.width, 6f), Border, 3f);
                fill = Shape(parent, label + " Fill", new Rect(Track.x, y, 1f, 6f), Teal, 3f);
                thumb = Shape(parent, label + " Thumb", new Rect(Track.x - 7f, y - 4f, 14f, 14f), Teal, 7f);
            }

            internal void Draw(float current, float capacity, bool hover)
            {
                float fraction = Pending ?? Mathf.Clamp01(current / capacity);
                float shown = Pending.HasValue ? fraction * capacity : current;
                string value = $"{shown:0.#} / {capacity:0.#} L ({fraction * 100f:0}%)";
                if (value != displayed)
                {
                    displayed = value;
                    amount.Set(value);
                }
                if (fraction != previousFraction)
                {
                    previousFraction = fraction;
                    float width = Track.width * fraction;
                    fill.enabled = width > 0f;
                    Place(fill.rectTransform, new Rect(Track.x, Track.center.y - 3f, width, 6f));
                    Place(thumb.rectTransform, new Rect(Track.x + width - 7f, Track.center.y - 7f, 14f, 14f));
                }
                thumb.color = hover ? Text : Teal;
                amount.Color = Pending.HasValue ? Teal : Muted;
            }
        }
    }
}
