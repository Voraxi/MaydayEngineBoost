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
    [BepInPlugin(PluginId, "Mayday Flight Controls", "1.8.0")]
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

            InterfaceText.Configure(Config);
            FuelEditor.Configure(Config);
            DebugBridge.Configure(Config);

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

    internal enum InterfaceLanguage
    {
        Auto,
        English,
        Chinese
    }

    internal static class InterfaceText
    {
        private static InterfaceLanguage language;

        internal static void Configure(ConfigFile config)
        {
            language = config.Bind("Interface", "Language", InterfaceLanguage.Auto,
                "F7 panel language. Auto follows the game's language. Options: Auto, English, Chinese. Restart after editing this setting.").Value;
        }

        internal static bool UseChinese => language == InterfaceLanguage.Chinese ||
            (language == InterfaceLanguage.Auto &&
             string.Equals(Loc.Lang, "schinese", StringComparison.OrdinalIgnoreCase));

        internal static string Localize(bool chinese, string english, string simplifiedChinese) =>
            chinese ? simplifiedChinese : english;
    }

    internal static class FuelEditor
    {
        private const float SampleSeconds = 1f;
        private static ConfigEntry<bool> lockEnabled;
        private static FuelSystem tracked;
        private static float mainTarget = -1f;
        private static float reserveTarget = -1f;
        private static float sampleTimer;
        private static float lastLevel = -1f;

        /// <summary>Smoothed main + reserve consumption in litres per minute, or NaN before the first sample.</summary>
        internal static float BurnPerMinute { get; private set; } = float.NaN;

        internal static void Configure(ConfigFile config)
        {
            lockEnabled = config.Bind("Fuel", "Lock", false,
                "Keep main and reserve fuel from dropping in flights you host. Toggle it in the F7 panel.");
        }

        internal static bool LockEnabled
        {
            get => lockEnabled != null && lockEnabled.Value;
            set
            {
                if (lockEnabled == null || lockEnabled.Value == value)
                    return;
                lockEnabled.Value = value;
                ResetTargets();
                lockEnabled.ConfigFile.Save();
            }
        }

        internal static bool Locking => LockEnabled && Active != null;

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

        /// <summary>Fuel system to read from: the host's when hosting, the synced copy otherwise.</summary>
        internal static FuelSystem Readable
        {
            get
            {
                FuelSystem fuel = SysRefs.Fuel;
                return fuel != null && fuel.Capacity > 0f ? fuel : null;
            }
        }

        internal static void ResetTargets()
        {
            mainTarget = -1f;
            reserveTarget = -1f;
        }

        internal static bool ApplyFromPanel(FuelSystem fuel, float? main, float? reserve)
        {
            if (!Apply(fuel, main, reserve))
                return false;
            // A deliberate edit becomes the new level the lock holds.
            ResetTargets();
            lastLevel = fuel.Total + fuel.Reserve;
            return true;
        }

        internal static void Tick(float deltaTime)
        {
            FuelSystem fuel = Readable;
            if (fuel == null)
            {
                tracked = null;
                BurnPerMinute = float.NaN;
                return;
            }

            if (fuel != tracked)
            {
                tracked = fuel;
                lastLevel = -1f;
                sampleTimer = 0f;
                BurnPerMinute = float.NaN;
                ResetTargets();
            }

            sampleTimer += deltaTime;
            if (sampleTimer < SampleSeconds)
                return;
            float elapsed = sampleTimer;
            sampleTimer = 0f;

            float level = fuel.Total + fuel.Reserve;
            if (lastLevel >= 0f && elapsed > 0f)
            {
                float used = lastLevel - level;
                // A rise means refuelling; it says nothing about burn, so skip that sample.
                if (used >= 0f)
                {
                    float rate = used / elapsed * 60f;
                    BurnPerMinute = float.IsNaN(BurnPerMinute) ? rate : Mathf.Lerp(BurnPerMinute, rate, 0.3f);
                }
            }
            lastLevel = level;

            FuelSystem active = Active;
            if (active == null || !LockEnabled)
            {
                ResetTargets();
                return;
            }

            float main = active.Total / (2f * active.Capacity);
            float reserve = active.Reserve / FuelSystem.ReserveCapacity;
            if (mainTarget < 0f)
            {
                mainTarget = main;
                reserveTarget = reserve;
                return;
            }

            // Refuelling above the locked level raises the lock instead of fighting it.
            mainTarget = Mathf.Max(mainTarget, main);
            reserveTarget = Mathf.Max(reserveTarget, reserve);
            bool refillMain = main < mainTarget - 0.0005f;
            bool refillReserve = reserve < reserveTarget - 0.0005f;
            if (!refillMain && !refillReserve)
                return;

            if (Apply(active, refillMain ? mainTarget : null, refillReserve ? reserveTarget : null))
                lastLevel = active.Total + active.Reserve;
        }

        private static bool Apply(FuelSystem fuel, float? main, float? reserve)
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

    /// <summary>
    /// Developer test hook, off by default. With [Debug] Bridge = true the mod writes its live state to
    /// BepInEx/flightcontrols-debug/state.txt and runs commands written to cmd.txt (one per line):
    /// open | close | toggle | click X Y (panel coordinates) | thrust V | lock on|off | fuel main|reserve F
    /// </summary>
    internal static class DebugBridge
    {
        private static ConfigEntry<bool> enabled;
        private static string folder;
        private static float nextWrite;
        private static float nextPoll;
        private static readonly System.Collections.Generic.Queue<string> pending = new System.Collections.Generic.Queue<string>();
        private static BepInEx.Logging.ManualLogSource log;

        internal static bool Enabled => enabled != null && enabled.Value;

        internal static void Configure(ConfigFile config)
        {
            enabled = config.Bind("Debug", "Bridge", false,
                "Developer testing only. Writes live state to BepInEx/flightcontrols-debug and reads commands from it.");
            if (!enabled.Value)
                return;
            folder = System.IO.Path.Combine(Paths.BepInExRootPath, "flightcontrols-debug");
            System.IO.Directory.CreateDirectory(folder);
            log = BepInEx.Logging.Logger.CreateLogSource("Flight Controls Debug");
            Log("Debug bridge enabled at " + folder);
        }

        internal static void Log(string message)
        {
            log?.LogInfo(message);
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "log.txt"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Logging must never break the game.
            }
        }

        /// <summary>Runs queued commands and returns a panel point to click this frame, if any.</summary>
        internal static Vector2? Poll(EngineBoostPanel panel)
        {
            float now = Time.unscaledTime;
            try
            {
                if (now >= nextPoll)
                {
                    nextPoll = now + 0.2f;
                    string commands = System.IO.Path.Combine(folder, "cmd.txt");
                    if (System.IO.File.Exists(commands))
                    {
                        foreach (string line in System.IO.File.ReadAllLines(commands))
                            if (!string.IsNullOrWhiteSpace(line))
                                pending.Enqueue(line.Trim());
                        System.IO.File.Delete(commands);
                    }
                }

                if (now >= nextWrite)
                {
                    nextWrite = now + 0.5f;
                    System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "state.txt"), panel.DebugState());
                }
            }
            catch (Exception error)
            {
                Log("bridge io error: " + error.Message);
            }

            // One command per frame so the UI updates between steps.
            if (pending.Count == 0)
                return null;
            string command = pending.Dequeue();
            Log("cmd: " + command);
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "open":
                    case "close":
                    case "toggle":
                        panel.DebugToggle();
                        return null;
                    case "click":
                        return new Vector2(float.Parse(parts[1], culture), float.Parse(parts[2], culture));
                    case "thrust":
                        EngineBoostPlugin.ThrustMultiplier.Value = float.Parse(parts[1], culture);
                        return null;
                    case "lock":
                        FuelEditor.LockEnabled = parts[1] == "on";
                        return null;
                    case "fuel":
                        float level = float.Parse(parts[2], culture);
                        bool ok = parts[1] == "main"
                            ? FuelEditor.ApplyFromPanel(FuelEditor.Active, level, null)
                            : FuelEditor.ApplyFromPanel(FuelEditor.Active, null, level);
                        Log("fuel apply result: " + ok);
                        return null;
                    default:
                        Log("unknown command");
                        return null;
                }
            }
            catch (Exception error)
            {
                Log("cmd failed: " + error);
                return null;
            }
        }
    }

    /// <summary>
    /// The F7 panel: a glass-cockpit style instrument card that slides in from the right.
    /// Every coordinate below is in panel space (origin top-left, y down, unscaled).
    /// </summary>
    public sealed class EngineBoostPanel : MonoBehaviour
    {
        private const float Width = 460f;
        private const float Height = 640f;
        private const float Margin = 24f;
        private const float Step = 0.05f;
        private const float OpenSeconds = 0.36f;
        private const float CloseSeconds = 0.2f;

        // Avionics palette: deep glass, cyan readouts, amber for stock/caution, green for locks.
        internal static readonly Color Glass = new Color32(9, 13, 19, 246);
        internal static readonly Color Card = new Color32(17, 24, 33, 255);
        internal static readonly Color Line = new Color32(40, 53, 68, 255);
        internal static readonly Color Control = new Color32(30, 40, 52, 255);
        internal static readonly Color Hover = new Color32(46, 62, 79, 255);
        internal static readonly Color Text = new Color32(234, 241, 247, 255);
        internal static readonly Color Muted = new Color32(126, 145, 163, 255);
        internal static readonly Color Faint = new Color32(72, 89, 106, 255);
        internal static readonly Color Cyan = new Color32(86, 214, 240, 255);
        internal static readonly Color Amber = new Color32(255, 186, 82, 255);
        internal static readonly Color Green = new Color32(108, 232, 152, 255);
        internal static readonly Color Red = new Color32(255, 104, 104, 255);

        private static readonly Rect CloseRect = new Rect(408f, 22f, 32f, 32f);
        private static readonly Rect Slider = new Rect(36f, 158f, 388f, 36f);
        private static readonly Rect MinusRect = new Rect(36f, 212f, 40f, 30f);
        private static readonly Rect PlusRect = new Rect(82f, 212f, 40f, 30f);
        private static readonly Rect ResetRect = new Rect(304f, 212f, 120f, 30f);

        private bool open;
        private float anim;
        private bool dragging;
        private bool settingsDirty;
        private bool chinese;
        private RectTransform canvasRoot;
        private RectTransform panel;
        private RectTransform body;
        private CanvasGroup bodyGroup;
        private readonly Layer[] layers = new Layer[4];
        private Image accent;
        private RectTransform controls;
        private RectTransform guest;
        private Button close;
        private Button minus;
        private Button plus;
        private Button reset;
        private Image track;
        private Image fill;
        private Image thumb;
        private Image thumbCore;
        private Image thumbHalo;
        private UIText number;
        private UIText delta;
        private Image deltaChip;
        private UIText session;
        private UIText role;
        private Image roleChip;
        private FuelControls fuelControls;
        private FlightReadout readout;
        private float shownThrust = float.NaN;
        private float thumbScale = 1f;

        public EngineBoostPanel(IntPtr pointer) : base(pointer) { }

        private static bool CanAdjust => NetworkServer.active || !NetworkClient.active;

        private static float PanelScale
        {
            get
            {
                float preferred = Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.75f);
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

        private static float EaseOutCubic(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t * t;
        }

        private static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            const float s = 1.4f;
            return 1f + t * t * ((s + 1f) * t + s);
        }

        /// <summary>Frame-rate independent approach factor for smoothing.</summary>
        internal static float Smooth(float speed, float dt) => 1f - Mathf.Exp(-speed * dt);

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            FuelEditor.Tick(Time.deltaTime);

            if (open && !UIState.IsOpen(this))
            {
                // The game closed us (for example with Escape).
                BeginClose(popState: false, "game closed it (UIState)");
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f7Key.wasPressedThisFrame)
            {
                if (open)
                    BeginClose(popState: true, "F7");
                else
                    Open();
            }

            Vector2? simulated = DebugBridge.Enabled ? DebugBridge.Poll(this) : null;

            if (canvasRoot == null || !canvasRoot.gameObject.activeSelf)
                return;

            Animate(dt);
            if (!canvasRoot.gameObject.activeSelf)
                return;

            bool canAdjust = CanAdjust;
            bool interactive = open && anim > 0.85f;
            float scale = PanelScale;
            Mouse mouse = Mouse.current;
            Vector2 pointer = interactive && mouse != null && scale > 0f
                ? PanelPointer(mouse, scale) : new Vector2(-1f, -1f);

            if (interactive && canAdjust && keyboard != null)
            {
                if (keyboard.homeKey.wasPressedThisFrame)
                    ApplyAndSave(1f);
                else if (keyboard.leftArrowKey.wasPressedThisFrame)
                    ApplyAndSave(EngineBoostPlugin.ThrustMultiplier.Value - Step);
                else if (keyboard.rightArrowKey.wasPressedThisFrame)
                    ApplyAndSave(EngineBoostPlugin.ThrustMultiplier.Value + Step);
            }

            if (simulated.HasValue)
            {
                DebugBridge.Log($"simulated click at ({simulated.Value.x}, {simulated.Value.y}) interactive={interactive}");
                if (interactive)
                {
                    // Same path as a real click: press, then release.
                    HandlePointerInput(simulated.Value, true, true, canAdjust);
                    HandlePointerInput(simulated.Value, false, false, canAdjust);
                }
            }

            if (interactive && mouse != null && HandleMouse(mouse, pointer, canAdjust))
                return;

            RefreshView(scale, pointer, canAdjust, dt);
        }

        internal string DebugState()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"time={Time.unscaledTime:0.0}");
            text.AppendLine($"panel.open={open} anim={anim:0.00} built={canvasRoot != null} chinese={chinese} scale={PanelScale:0.000}");
            text.AppendLine($"net.server={NetworkServer.active} net.client={NetworkClient.active} canAdjust={CanAdjust}");
            text.AppendLine($"thrust={EngineBoostPlugin.ThrustMultiplier.Value:0.00}");
            FuelSystem readable = FuelEditor.Readable;
            FuelSystem active = FuelEditor.Active;
            text.AppendLine($"fuel.readable={readable != null} fuel.active={active != null} lock.enabled={FuelEditor.LockEnabled} locking={FuelEditor.Locking}");
            if (readable != null)
                text.AppendLine($"fuel.main={readable.Total:0.00}/{2f * readable.Capacity:0} fuel.reserve={readable.Reserve:0.00}/{FuelSystem.ReserveCapacity:0} burn/min={FuelEditor.BurnPerMinute:0.000}");
            PlaneFlight flight = PlaneFlight.Instance;
            FlightSim sim = flight != null ? flight.sim : null;
            if (sim != null)
                text.AppendLine($"sim.speed={sim.Speed:0.00} sim.agl={sim.Agl:0.00} sim.onGround={sim.OnGround} sim.overspeed={sim.Overspeed}");
            else
                text.AppendLine("sim=none");
            if (fuelControls != null)
                text.AppendLine("ui.fuel " + fuelControls.DebugText());
            if (readout != null)
                text.AppendLine("ui.readout " + readout.DebugText());
            if (number != null)
                text.AppendLine($"ui.thrust number='{number.Text}' delta='{delta.Text}' session='{session.Text}' role='{role.Text}'");
            return text.ToString();
        }

        internal void DebugToggle() { if (open) BeginClose(popState: true, "debug command"); else Open(); }

        private void Animate(float dt)
        {
            float target = open ? 1f : 0f;
            anim = Mathf.MoveTowards(anim, target, dt / (open ? OpenSeconds : CloseSeconds));
            if (!open && anim <= 0f)
            {
                canvasRoot.gameObject.SetActive(false);
                return;
            }

            float eased = open ? EaseOutBack(anim) : EaseOutCubic(anim);
            bodyGroup.alpha = Mathf.Clamp01(anim * 1.8f);
            body.anchoredPosition = new Vector2((1f - eased) * 56f, 0f);
            float bodyScale = Mathf.LerpUnclamped(0.95f, 1f, eased);
            body.localScale = new Vector3(bodyScale, bodyScale, 1f);

            // Cards cascade in one after another.
            for (int index = 0; index < layers.Length; index++)
            {
                Layer layer = layers[index];
                if (layer == null)
                    continue;
                float delay = 0.1f * index;
                float local = open ? Mathf.Clamp01((anim - delay) / (1f - delay)) : anim;
                float e = EaseOutCubic(local);
                layer.Group.alpha = e;
                layer.Rect.anchoredPosition = new Vector2(0f, -(1f - e) * 14f);
            }

            Place(accent.rectTransform, new Rect(20f, 22f, 3f, 34f * EaseOutCubic(anim)));
        }

        private bool HandleMouse(Mouse mouse, Vector2 pointer, bool canAdjust)
        {
            bool pressed = mouse.leftButton.wasPressedThisFrame;
            if (pressed && DebugBridge.Enabled)
                DebugBridge.Log($"mouse press at panel ({pointer.x:0.0}, {pointer.y:0.0}) screen {mouse.position.ReadValue()} canAdjust={canAdjust}");
            return HandlePointerInput(pointer, pressed, mouse.leftButton.isPressed, canAdjust);
        }

        private bool HandlePointerInput(Vector2 pointer, bool pressed, bool held, bool canAdjust)
        {
            if (pressed)
            {
                if (CloseRect.Contains(pointer))
                {
                    BeginClose(popState: true, "close button");
                    return true;
                }
                if (canAdjust)
                {
                    float current = EngineBoostPlugin.ThrustMultiplier.Value;
                    if (ResetRect.Contains(pointer))
                    {
                        ApplyAndSave(1f);
                        reset.Flash(Amber);
                    }
                    else if (MinusRect.Contains(pointer))
                    {
                        ApplyAndSave(current - Step);
                        minus.Flash(Cyan);
                    }
                    else if (PlusRect.Contains(pointer))
                    {
                        ApplyAndSave(current + Step);
                        plus.Flash(Cyan);
                    }

                    dragging = Slider.Contains(pointer);
                }
            }

            if (canAdjust)
                fuelControls.HandlePointer(pointer, pressed, held);

            if (dragging && canAdjust && held)
            {
                float ratio = Mathf.InverseLerp(Slider.xMin + 6f, Slider.xMax - 6f, pointer.x);
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
            bool useChinese = InterfaceText.UseChinese;
            if (canvasRoot != null && chinese != useChinese)
            {
                canvasRoot.gameObject.SetActive(false);
                Destroy(canvasRoot.gameObject);
                canvasRoot = null;
            }
            chinese = useChinese;

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
                anim = 0f;
                shownThrust = float.NaN;
            }

            UIState.Push(this);
            open = true;
            canvasRoot.gameObject.SetActive(true);
            if (anim <= 0f)
                Animate(0f);
            RefreshView(PanelScale, new Vector2(-1f, -1f), CanAdjust, 0f);
        }

        private void BeginClose(bool popState, string reason)
        {
            if (open)
            {
                if (DebugBridge.Enabled)
                    DebugBridge.Log("panel closed: " + reason);
                open = false;
                dragging = false;
                fuelControls?.Clear();
                if (popState)
                    UIState.Pop(this);
            }
            SaveSetting();
        }

        private void OnDisable()
        {
            BeginClose(popState: true, "component disabled");
            anim = 0f;
            if (canvasRoot != null)
                canvasRoot.gameObject.SetActive(false);
        }

        private void BuildView()
        {
            canvasRoot = UIF.Rect("Flight Controls Canvas", transform);
            canvasRoot.gameObject.SetActive(false);
            Canvas canvas = canvasRoot.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 28000;

            panel = UIF.Rect("Flight Controls Panel", canvasRoot);
            Vector2 topRight = new Vector2(1f, 1f);
            UIF.At(panel, topRight, topRight, new Vector2(-Margin, -Margin), new Vector2(Width, Height));

            body = UIF.Rect("Body", panel);
            UIF.Stretch(body);
            body.pivot = new Vector2(1f, 0.5f);
            bodyGroup = body.gameObject.AddComponent<CanvasGroup>();

            UIF.ShadowFor(body, 24f, 26f, -12f, 0.55f);
            UIF.Stretch(UIF.Round(body, "Glass Edge", Line, 22f).rectTransform);
            UIF.Stretch(UIF.Round(body, "Glass", Glass, 21f).rectTransform, 1f, 1f, 1f, 1f);
            // Thin rule under the header, inset from the rounded corners.
            Shape(body, "Header Rule", new Rect(20f, 66f, Width - 40f, 1f), Line, 0f);

            // Header
            layers[0] = new Layer(body, "Header");
            RectTransform header = layers[0].Rect;
            accent = Shape(header, "Accent", new Rect(20f, 22f, 3f, 34f), Cyan, 1.5f);
            UIText title = TextAt(header, L("FLIGHT CONTROLS", "飞行控制"), new Rect(34f, 18f, 280f, 26f), 19f, Text, bold: true);
            Spaced(title, chinese ? 2f : 4f);
            TextAt(header, L("Mayday Flight Controls  ·  v1.8", "Mayday 飞行控制  ·  v1.8"),
                new Rect(34f, 42f, 280f, 16f), 11f, Muted);
            roleChip = Shape(header, "Role Chip", new Rect(318f, 27f, 82f, 22f), WithAlpha(Green, 0.14f), 11f);
            role = TextAt(roleChip.rectTransform, "", new Rect(0f, 0f, 82f, 22f), 11f, Green, TextAlignmentOptions.Center, true);
            Spaced(role, 1.5f);
            close = new Button(header, CloseRect, "×", 20f, 10f);

            // Thrust card
            layers[1] = new Layer(body, "Thrust");
            controls = layers[1].Rect;
            CardAt(controls, new Rect(20f, 76f, 420f, 180f));
            Spaced(TextAt(controls, L("ENGINE THRUST", "发动机推力"), new Rect(36f, 88f, 200f, 16f), 11f, Muted, bold: true), 2f);
            number = TextAt(controls, "", new Rect(34f, 102f, 240f, 54f), 46f, Cyan, bold: true, display: true);
            deltaChip = Shape(controls, "Delta Chip", new Rect(296f, 106f, 128f, 24f), WithAlpha(Amber, 0.14f), 12f);
            delta = TextAt(deltaChip.rectTransform, "", new Rect(0f, 0f, 128f, 24f), 12f, Amber, TextAlignmentOptions.Center, true);
            session = TextAt(controls, "", new Rect(216f, 134f, 208f, 16f), 11f, Muted, TextAlignmentOptions.MidlineRight);

            float center = Slider.center.y;
            track = Shape(controls, "Track", new Rect(Slider.x, center - 3f, Slider.width, 6f), Line, 3f);
            fill = Shape(controls, "Fill", new Rect(Slider.x, center - 3f, 1f, 6f), Cyan, 3f);
            for (int step = 1; step <= 6; step++)
            {
                float x = SliderX(step);
                bool stock = step == 1;
                Shape(controls, "Tick", new Rect(x - 0.5f, center + 8f, 1f, stock ? 8f : 5f), stock ? Amber : Faint, 0f);
            }
            float stockX = SliderX(1f);
            Shape(controls, "Stock Marker", new Rect(stockX - 1.5f, center - 9f, 3f, 18f), Amber, 1.5f);
            TextAt(controls, $"{EngineBoostPlugin.MinimumThrust * 100f:0}%", new Rect(Slider.x - 4f, center + 15f, 40f, 14f), 10f, Faint);
            TextAt(controls, L("STOCK", "原版"), new Rect(stockX - 30f, center + 15f, 60f, 14f), 10f, Amber, TextAlignmentOptions.Center, true);
            TextAt(controls, "300%", new Rect(SliderX(3f) - 30f, center + 15f, 60f, 14f), 10f, Faint, TextAlignmentOptions.Center);
            TextAt(controls, $"{EngineBoostPlugin.MaximumThrust * 100f:0}%", new Rect(Slider.xMax - 44f, center + 15f, 48f, 14f), 10f, Faint, TextAlignmentOptions.MidlineRight);
            thumbHalo = Shape(controls, "Thumb Halo", new Rect(0f, 0f, 34f, 34f), WithAlpha(Cyan, 0.18f), 17f);
            thumb = Shape(controls, "Thumb", new Rect(0f, 0f, 20f, 20f), Cyan, 10f);
            thumbCore = Shape(thumb.rectTransform, "Thumb Core", new Rect(5f, 5f, 10f, 10f), Card, 5f);

            minus = new Button(controls, MinusRect, "−", 20f, 8f);
            plus = new Button(controls, PlusRect, "+", 20f, 8f);
            reset = new Button(controls, ResetRect, L("Reset to stock", "恢复原版"), 12f, 8f);

            // Fuel card
            layers[2] = new Layer(body, "Fuel");
            fuelControls = new FuelControls(layers[2].Rect, chinese);

            // Guest card covers thrust and fuel when someone else hosts.
            guest = UIF.Rect("Guest", layers[1].Rect);
            UIF.Stretch(guest);
            CardAt(guest, new Rect(20f, 76f, 420f, 418f));
            Shape(guest, "Guest Icon Ring", new Rect(206f, 200f, 48f, 48f), WithAlpha(Amber, 0.16f), 24f);
            TextAt(guest, "!", new Rect(206f, 200f, 48f, 48f), 26f, Amber, TextAlignmentOptions.Center, true);
            TextAt(guest, L("The host is flying this one", "由房主控制这趟航班"),
                new Rect(36f, 262f, 388f, 30f), 20f, Text, TextAlignmentOptions.Center, true);
            TextAt(guest, L("Thrust and fuel controls unlock when you host a lobby.", "你创建房间后即可调整推力和燃油。"),
                new Rect(36f, 296f, 388f, 20f), 13f, Muted, TextAlignmentOptions.Center);

            // Flight readout
            layers[3] = new Layer(body, "Flight");
            readout = new FlightReadout(layers[3].Rect, chinese);
            TextAt(layers[3].Rect, L("F7  close     Left / Right  adjust     Home  reset", "F7  关闭     左 / 右  调整     Home  恢复"),
                new Rect(20f, 612f, 420f, 16f), 11f, Faint, TextAlignmentOptions.Center);
        }

        private void RefreshView(float scale, Vector2 pointer, bool canAdjust, float dt)
        {
            if (panel.localScale.x != scale)
                panel.localScale = new Vector3(scale, scale, 1f);

            bool server = NetworkServer.active;
            bool guestView = !canAdjust;
            if (guest.gameObject.activeSelf != guestView)
                guest.gameObject.SetActive(guestView);
            fuelControls.SetVisible(!guestView);

            Color roleColor = server ? Green : NetworkClient.active ? Amber : Muted;
            role.Set(server ? L("HOST", "房主") : NetworkClient.active ? L("GUEST", "客人") : L("MENU", "菜单"));
            role.Color = roleColor;
            roleChip.color = WithAlpha(roleColor, 0.14f);

            close.Update(close.Rect.Contains(pointer), dt);
            readout.Refresh(dt);
            if (guestView)
                return;

            fuelControls.Refresh(pointer, FuelEditor.Active, dt);

            float current = EngineBoostPlugin.ThrustMultiplier.Value;
            bool stock = Mathf.Approximately(current, 1f);
            shownThrust = float.IsNaN(shownThrust) || dt <= 0f
                ? current : Mathf.Lerp(shownThrust, current, Smooth(16f, dt));
            if (Mathf.Abs(shownThrust - current) < 0.002f)
                shownThrust = current;

            number.Set($"{shownThrust * 100f:0}%");
            Color tone = stock ? Amber : current > 1f ? Cyan : Red;
            number.Color = Color.Lerp(number.Color, tone, dt <= 0f ? 1f : Smooth(10f, dt));
            string change = $"{(current - 1f) * 100f:+0;-0;0}%";
            delta.Set(stock ? L("STOCK THRUST", "原版推力") : L($"{change} VS STOCK", $"比原版 {change}"));
            delta.Color = tone;
            deltaChip.color = WithAlpha(tone, 0.14f);
            session.Set(server ? L("Live on your flight", "正在作用于你的航班") : L("Applies when you host", "创建房间后生效"));

            float width = Mathf.Max(1f, SliderX(shownThrust) - Slider.x);
            float center = Slider.center.y;
            Place(fill.rectTransform, new Rect(Slider.x, center - 3f, width, 6f));
            fill.color = tone;
            float x = Slider.x + width;
            bool hot = dragging || Slider.Contains(pointer);
            thumbScale = Mathf.Lerp(thumbScale, hot ? 1.2f : 1f, dt <= 0f ? 1f : Smooth(18f, dt));
            float size = 20f * thumbScale;
            Place(thumb.rectTransform, new Rect(x - size / 2f, center - size / 2f, size, size));
            thumb.color = tone;
            Place(thumbCore.rectTransform, new Rect(size / 4f, size / 4f, size / 2f, size / 2f));
            float halo = 34f * thumbScale;
            Place(thumbHalo.rectTransform, new Rect(x - halo / 2f, center - halo / 2f, halo, halo));
            thumbHalo.color = WithAlpha(tone, hot ? 0.22f : 0f);

            minus.Update(MinusRect.Contains(pointer) && current > EngineBoostPlugin.MinimumThrust, dt);
            plus.Update(PlusRect.Contains(pointer) && current < EngineBoostPlugin.MaximumThrust, dt);
            reset.Update(ResetRect.Contains(pointer) && !stock, dt);
            reset.SetDimmed(stock);
        }

        private static float SliderX(float value) =>
            Mathf.Lerp(Slider.xMin + 6f, Slider.xMax - 6f,
                Mathf.InverseLerp(EngineBoostPlugin.MinimumThrust, EngineBoostPlugin.MaximumThrust, value));

        private string L(string english, string simplifiedChinese) =>
            InterfaceText.Localize(chinese, english, simplifiedChinese);

        internal static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        internal static void CardAt(RectTransform parent, Rect rect)
        {
            Shape(parent, "Card", rect, Card, 14f);
            Image edge = UIF.Outline(parent, "Card Edge", Line, 14f);
            Place(edge.rectTransform, rect);
        }

        internal static Image Shape(Transform parent, string name, Rect rect, Color color, float radius)
        {
            Image image = radius > 0f
                ? UIF.Round(parent, name, color, radius)
                : UIF.Image(parent, name, null, color);
            Place(image.rectTransform, rect);
            return image;
        }

        internal static UIText TextAt(Transform parent, string text, Rect rect, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false, bool display = false)
        {
            UIText label = UIF.Text(parent, "Text", text, size, color, align, display);
            if (bold)
                label.Tmp.fontStyle = FontStyles.Bold;
            Place(label.Rect, rect);
            return label;
        }

        internal static UIText Spaced(UIText label, float spacing)
        {
            label.Tmp.characterSpacing = spacing;
            return label;
        }

        internal static void Place(RectTransform element, Rect rect)
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
            float snapped = Mathf.Clamp(Mathf.Round(value / Step) * Step,
                EngineBoostPlugin.MinimumThrust, EngineBoostPlugin.MaximumThrust);
            if (Mathf.Approximately(snapped, EngineBoostPlugin.ThrustMultiplier.Value))
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

        /// <summary>A full-panel layer with its own fade so cards can cascade in.</summary>
        private sealed class Layer
        {
            internal readonly RectTransform Rect;
            internal readonly CanvasGroup Group;

            internal Layer(RectTransform parent, string name)
            {
                Rect = UIF.Rect(name, parent);
                UIF.Stretch(Rect);
                Group = Rect.gameObject.AddComponent<CanvasGroup>();
            }
        }

        /// <summary>A rounded button whose fill eases between idle, hover and a brief press flash.</summary>
        internal sealed class Button
        {
            internal readonly Rect Rect;
            private readonly Image fillImage;
            private readonly UIText label;
            private Color current = Control;
            private Color flashColor;
            private float flash;
            private bool dimmed;

            internal Button(Transform parent, Rect rect, string text, float fontSize, float radius)
            {
                Rect = rect;
                fillImage = Shape(parent, text, rect, Control, radius);
                label = UIF.Text(fillImage.rectTransform, "Label", text, fontSize, Text, TextAlignmentOptions.Center);
                UIF.Stretch(label.Rect);
            }

            internal void SetLabel(string text) => label.Set(text);

            internal void SetDimmed(bool value) => dimmed = value;

            internal void Flash(Color color)
            {
                flashColor = color;
                flash = 1f;
            }

            internal void Update(bool hover, float dt)
            {
                Color target = dimmed ? Card : hover ? Hover : Control;
                current = dt <= 0f ? target : Color.Lerp(current, target, Smooth(16f, dt));
                flash = Mathf.MoveTowards(flash, 0f, dt * 2.6f);
                fillImage.color = flash > 0f ? Color.Lerp(current, WithAlpha(flashColor, 0.55f), flash) : current;
                label.Color = dimmed ? Faint : Text;
            }
        }

        private sealed class FuelControls
        {
            private static readonly Rect SwitchRect = new Rect(378f, 279f, 46f, 24f);
            private static readonly Rect ApplyRect = new Rect(36f, 448f, 388f, 34f);
            private readonly bool chinese;
            private readonly RectTransform root;
            private readonly RectTransform editor;
            private readonly UIText message;
            private readonly FuelGauge main;
            private readonly FuelGauge reserve;
            private readonly Button apply;
            private readonly Image switchTrack;
            private readonly Image switchKnob;
            private readonly UIText switchLabel;
            private readonly UIText note;
            private readonly Image lockGlow;
            private FuelSystem fuel;
            private FuelGauge dragged;
            private float knob;
            private float appliedTimer;

            internal FuelControls(RectTransform parent, bool chinese)
            {
                this.chinese = chinese;
                root = UIF.Rect("Fuel Card", parent);
                UIF.Stretch(root);
                lockGlow = Shape(root, "Lock Glow", new Rect(17f, 265f, 426f, 230f), WithAlpha(Green, 0f), 16f);
                CardAt(root, new Rect(20f, 268f, 420f, 226f));
                Spaced(TextAt(root, L("FUEL", "燃油"), new Rect(36f, 282f, 200f, 16f), 11f, Muted, bold: true), 2f);
                message = TextAt(root, L("Host a loaded flight to edit fuel.", "作为房主加载航班后即可调整燃油。"),
                    new Rect(36f, 362f, 388f, 24f), 14f, Muted, TextAlignmentOptions.Center);

                editor = UIF.Rect("Fuel Editor", root);
                UIF.Stretch(editor);
                switchLabel = TextAt(editor, "", new Rect(248f, 283f, 122f, 16f), 11f, Muted, TextAlignmentOptions.MidlineRight, true);
                Spaced(switchLabel, 1.5f);
                switchTrack = Shape(editor, "Lock Switch", SwitchRect, Control, 12f);
                switchKnob = Shape(editor, "Lock Knob", new Rect(SwitchRect.x + 3f, SwitchRect.y + 3f, 18f, 18f), Muted, 9f);
                main = new FuelGauge(editor, L("Main tanks", "主油箱"), 316f);
                reserve = new FuelGauge(editor, L("Reserve", "备用油箱"), 372f);
                note = TextAt(editor, "", new Rect(36f, 424f, 388f, 16f), 11f, Muted);
                apply = new Button(editor, ApplyRect, L("Apply fuel", "应用油量"), 13f, 9f);
            }

            private string L(string english, string simplifiedChinese) =>
                InterfaceText.Localize(chinese, english, simplifiedChinese);

            internal void SetVisible(bool visible)
            {
                if (root.gameObject.activeSelf != visible)
                    root.gameObject.SetActive(visible);
            }

            internal void HandlePointer(Vector2 position, bool pressed, bool held)
            {
                if (fuel == null || fuel != FuelEditor.Active)
                    return;

                if (pressed)
                {
                    if (SwitchRect.Contains(position) || SwitchLabelRect.Contains(position))
                    {
                        FuelEditor.LockEnabled = !FuelEditor.LockEnabled;
                        return;
                    }

                    if (ApplyRect.Contains(position) && (main.Pending.HasValue || reserve.Pending.HasValue))
                    {
                        if (FuelEditor.ApplyFromPanel(fuel, main.Pending, reserve.Pending))
                        {
                            Clear();
                            apply.Flash(Green);
                            appliedTimer = 1.4f;
                        }
                        return;
                    }

                    dragged = main.Hit.Contains(position) ? main : reserve.Hit.Contains(position) ? reserve : null;
                }

                if (dragged != null && (pressed || held))
                    dragged.Pending = Mathf.Round(100f * Mathf.InverseLerp(dragged.Track.xMin, dragged.Track.xMax, position.x)) / 100f;
                else
                    dragged = null;
            }

            private static readonly Rect SwitchLabelRect = new Rect(248f, 279f, 130f, 24f);

            internal void Refresh(Vector2 position, FuelSystem activeFuel, float dt)
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

                bool locking = ready && FuelEditor.LockEnabled;
                float glowTarget = locking ? 0.10f + 0.05f * Mathf.Sin(Time.unscaledTime * 2.4f) : 0f;
                Color glow = lockGlow.color;
                glow.a = dt <= 0f ? glowTarget : Mathf.Lerp(glow.a, glowTarget, Smooth(8f, dt));
                lockGlow.color = glow;
                if (!ready)
                    return;

                knob = dt <= 0f ? (locking ? 1f : 0f) : Mathf.Lerp(knob, locking ? 1f : 0f, Smooth(18f, dt));
                Place(switchKnob.rectTransform, new Rect(SwitchRect.x + 3f + 22f * knob, SwitchRect.y + 3f, 18f, 18f));
                bool switchHover = SwitchRect.Contains(position) || SwitchLabelRect.Contains(position);
                switchTrack.color = Color.Lerp(switchHover ? Hover : Control, WithAlpha(Green, 0.85f), knob);
                switchKnob.color = Color.Lerp(Muted, Card, knob);
                switchLabel.Set(locking ? L("FUEL LOCK ON", "燃油锁定 开") : L("FUEL LOCK", "燃油锁定"));
                switchLabel.Color = locking ? Green : Muted;

                main.Draw(fuel.Total, 2f * fuel.Capacity, main == dragged || main.Hit.Contains(position), locking, dt);
                reserve.Draw(fuel.Reserve, FuelSystem.ReserveCapacity, reserve == dragged || reserve.Hit.Contains(position), locking, dt);

                bool changed = main.Pending.HasValue || reserve.Pending.HasValue;
                appliedTimer = Mathf.MoveTowards(appliedTimer, 0f, dt);
                apply.SetLabel(appliedTimer > 0f ? L("Fuel applied", "已应用") : L("Apply fuel", "应用油量"));
                apply.SetDimmed(!changed && appliedTimer <= 0f);
                apply.Update(changed && ApplyRect.Contains(position), dt);

                note.Set(locking
                    ? L("Locked. Fuel won't drop below these levels.", "已锁定。油量不会低于当前水平。")
                    : changed ? L("Drag to set a level, then apply.", "拖动设置油量，然后应用。")
                    : L("Drag a gauge to add or remove fuel.", "拖动油量条以加油或减油。"));
                note.Color = locking ? Green : Muted;
            }

            internal string DebugText() =>
                $"fuel.bound={fuel != null} editorShown={editor.gameObject.activeSelf} pending.main={main.Pending?.ToString("0.00") ?? "-"} " +
                $"pending.reserve={reserve.Pending?.ToString("0.00") ?? "-"} switch='{switchLabel.Text}' note='{note.Text}' knob={knob:0.00}";

            internal void Clear()
            {
                main.Pending = null;
                reserve.Pending = null;
                dragged = null;
            }
        }

        private sealed class FuelGauge
        {
            internal readonly Rect Track;
            internal readonly Rect Hit;
            internal float? Pending;
            private readonly UIText amount;
            private readonly Image fillImage;
            private readonly Image ghost;
            private readonly Image thumbImage;
            private string displayed;
            private float shown = -1f;

            internal FuelGauge(RectTransform parent, string label, float top)
            {
                Track = new Rect(36f, top + 28f, 388f, 8f);
                Hit = new Rect(30f, top + 18f, 400f, 28f);
                TextAt(parent, label, new Rect(36f, top, 160f, 20f), 14f, Text, bold: true);
                amount = TextAt(parent, "", new Rect(196f, top, 228f, 20f), 13f, Muted, TextAlignmentOptions.MidlineRight);
                Shape(parent, label + " Track", Track, Line, 4f);
                fillImage = Shape(parent, label + " Fill", new Rect(Track.x, Track.y, 1f, Track.height), Cyan, 4f);
                ghost = Shape(parent, label + " Current", new Rect(Track.x, Track.y - 4f, 2f, Track.height + 8f), Text, 1f);
                for (int quarter = 1; quarter < 4; quarter++)
                    Shape(parent, "Quarter", new Rect(Track.x + Track.width * quarter / 4f - 0.5f, Track.yMax + 4f, 1f, 4f), Faint, 0f);
                thumbImage = Shape(parent, label + " Thumb", new Rect(0f, 0f, 14f, 14f), Text, 7f);
            }

            internal void Draw(float current, float capacity, bool hover, bool locked, float dt)
            {
                float actual = Mathf.Clamp01(current / capacity);
                float target = Pending ?? actual;
                shown = shown < 0f || dt <= 0f ? target : Mathf.Lerp(shown, target, Smooth(14f, dt));

                string value = Pending.HasValue
                    ? $"{current:0} > {target * capacity:0} L  ({target * 100f:0}%)"
                    : $"{current:0.#} / {capacity:0} L  ({actual * 100f:0}%)";
                if (value != displayed)
                {
                    displayed = value;
                    amount.Set(value);
                }
                amount.Color = Pending.HasValue ? Cyan : Muted;

                Color tone = Pending.HasValue ? Cyan : locked ? Green : actual < 0.15f ? Red : actual < 0.3f ? Amber : Cyan;
                if (actual < 0.15f && !Pending.HasValue && !locked)
                    tone = Color.Lerp(tone, Text, 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f));
                float width = Track.width * shown;
                fillImage.enabled = width > 1f;
                Place(fillImage.rectTransform, new Rect(Track.x, Track.y, Mathf.Max(width, 1f), Track.height));
                fillImage.color = tone;

                ghost.enabled = Pending.HasValue;
                if (Pending.HasValue)
                    Place(ghost.rectTransform, new Rect(Track.x + Track.width * actual - 1f, Track.y - 4f, 2f, Track.height + 8f));

                float size = hover ? 16f : 12f;
                Place(thumbImage.rectTransform, new Rect(Track.x + width - size / 2f, Track.center.y - size / 2f, size, size));
                thumbImage.color = hover ? Text : tone;
            }
        }

        private sealed class FlightReadout
        {
            private const float Top = 506f;
            private readonly bool chinese;
            private readonly Tile speed;
            private readonly Tile altitude;
            private readonly Tile burn;
            private readonly Tile endurance;
            private readonly Image live;
            private readonly UIText liveLabel;
            private float refresh;

            internal string DebugText() =>
                $"status='{liveLabel.Text}' speed={speed.DebugText()} alt={altitude.DebugText()} burn={burn.DebugText()} endurance={endurance.DebugText()}";

            internal FlightReadout(RectTransform parent, bool chinese)
            {
                this.chinese = chinese;
                CardAt(parent, new Rect(20f, Top, 420f, 96f));
                live = Shape(parent, "Live", new Rect(36f, Top + 17f, 6f, 6f), Green, 3f);
                Spaced(TextAt(parent, L("FLIGHT", "飞行数据"), new Rect(48f, Top + 12f, 120f, 16f), 11f, Muted, bold: true), 2f);
                liveLabel = TextAt(parent, "", new Rect(224f, Top + 12f, 200f, 16f), 10f, Faint, TextAlignmentOptions.MidlineRight, true);
                Spaced(liveLabel, 1.5f);
                float width = (388f - 3f * 8f) / 4f;
                speed = new Tile(parent, L("SPEED", "速度"), 36f, Top + 34f, width);
                altitude = new Tile(parent, L("ALTITUDE", "离地高度"), 36f + (width + 8f), Top + 34f, width);
                burn = new Tile(parent, L("BURN", "油耗"), 36f + 2f * (width + 8f), Top + 34f, width);
                endurance = new Tile(parent, L("ENDURANCE", "续航"), 36f + 3f * (width + 8f), Top + 34f, width);
            }

            private string L(string english, string simplifiedChinese) =>
                InterfaceText.Localize(chinese, english, simplifiedChinese);

            internal void Refresh(float dt)
            {
                PlaneFlight flight = PlaneFlight.Instance;
                FlightSim sim = flight != null ? flight.sim : null;
                bool active = sim != null;
                live.color = active
                    ? WithAlpha(Green, 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f)))
                    : Faint;

                refresh -= dt;
                if (refresh > 0f && dt > 0f)
                    return;
                refresh = 0.2f;

                if (!active)
                {
                    liveLabel.Set(L("NO FLIGHT", "未在飞行"));
                    speed.Set("--", "");
                    altitude.Set("--", "");
                }
                else
                {
                    liveLabel.Set(sim.OnGround ? L("ON GROUND", "地面") : L("AIRBORNE", "空中"));
                    float metres = sim.Speed;
                    speed.Set($"{metres:0}", L($"m/s · {metres * 1.94384f:0} kt", $"米/秒 · {metres * 1.94384f:0} 节"),
                        sim.Overspeed ? Red : Text);
                    altitude.Set($"{Mathf.Max(0f, sim.Agl):0}", L("m above ground", "米"), Text);
                }

                FuelSystem fuel = FuelEditor.Readable;
                float rate = FuelEditor.BurnPerMinute;
                bool burning = fuel != null && !float.IsNaN(rate) && rate > 0.01f;
                burn.Set(burning ? $"{rate:0.0}" : "--", burning ? L("L per min", "升/分钟") : "");

                if (FuelEditor.Locking)
                {
                    endurance.Set(L("LOCK", "锁定"), L("fuel locked", "燃油已锁定"), Green);
                }
                else if (burning)
                {
                    float minutes = (fuel.Total + fuel.Reserve) / rate;
                    int hours = (int)(minutes / 60f);
                    int rest = (int)(minutes % 60f);
                    endurance.Set(hours > 99 ? "99+" : $"{hours}:{rest:00}", L("hours left", "小时"),
                        minutes < 5f ? Red : minutes < 15f ? Amber : Text);
                }
                else
                {
                    endurance.Set("--", "");
                }
            }

            private sealed class Tile
            {
                private readonly UIText value;
                private readonly UIText unit;

                internal Tile(RectTransform parent, string label, float x, float y, float width)
                {
                    Spaced(TextAt(parent, label, new Rect(x, y, width, 12f), 9f, Faint, bold: true), 1.2f);
                    value = TextAt(parent, "--", new Rect(x, y + 12f, width, 28f), 22f, Text, bold: true, display: true);
                    unit = TextAt(parent, "", new Rect(x, y + 40f, width, 14f), 10f, Muted);
                }

                internal string DebugText() => $"'{value.Text}|{unit.Text}'";

                internal void Set(string text, string units, Color? color = null)
                {
                    value.Set(text);
                    value.Color = color ?? Text;
                    unit.Set(units);
                }
            }
        }
    }
}
