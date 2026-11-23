# Mayday Engine Boost

A Windows mod for the **Mayday Airways Demo** on Steam. It adds an in-game thrust slider and fuel editor for the host, plus a six-slot hotbar for the host's player.

[Download the latest release](https://github.com/Voraxi/MaydayEngineBoost/releases/latest)

## Install

You need the **Steam demo for Windows** and **BepInEx 6 for Unity IL2CPP, Windows x64**. This mod was built and tested against Steam build **26546546** and BepInEx **6.0.0-be.793**.

1. In your Steam Library, right-click **Mayday Airways Demo**, then select **Manage → Browse local files**. This opens the folder containing `MaydayAirways.exe`.
2. If you already have BepInEx 6 IL2CPP working for this demo, skip to step 4. Otherwise, use the [BepInEx installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html) to download the **Unity.IL2CPP-win-x64** archive. Extract its contents directly into the folder containing `MaydayAirways.exe`. You should see `winhttp.dll` and the `BepInEx` folder beside the game executable.
3. Launch the demo through Steam once. Let it reach the menu so BepInEx can create its files, then exit. The first launch may take longer than usual.
4. On the [latest release page](https://github.com/Voraxi/MaydayEngineBoost/releases/latest), expand **Assets** and download **MaydayEngineBoost.zip**. Extract it and copy `MaydayEngineBoost.dll` into the demo's `BepInEx\plugins` folder.
5. Relaunch the demo through Steam. Press **F7** to open the controls.

**Updating?** Close the game, download the latest release ZIP, and replace the old `MaydayEngineBoost.dll` in `BepInEx\plugins`. Keep a single copy of the plugin there.

## Use the mod

| Feature | Controls |
| --- | --- |
| Thrust | Press **F7**. Drag the slider, click **−** or **+**, or use **←** and **→**. The setting goes from 10% to 600% in five-point steps. **Reset to stock** or **Home** restores 100%. |
| Fuel | In a loaded flight you host, press **F7**, set **Main tanks** or **Reserve**, then click **Apply fuel**. The sliders show liters and percentages. Editing the mains sets both wing tanks to the same fill percentage. |
| Inventory | In solo play or as host, press **5** or **6** to select an extra slot. The scroll wheel cycles through slots 1–4. |

The host installs the mod for co-op. The host controls thrust and fuel, and the extra inventory slots belong to the host's player. The controls and fuel synchronization passed tests in an isolated copy of the game. Live Steam co-op is still untested.

Extra airborne thrust tapers between **74 and 80 m/s** to avoid triggering the stock overspeed climb. The game's overspeed protections remain active.

## Troubleshooting

- **F7 does nothing:** Confirm the DLL is directly in `BepInEx\plugins` and that BepInEx is the **IL2CPP Windows x64** build. Check `BepInEx\LogOutput.log` for `Loading [Mayday Engine Boost`. If the log lists an older version, check for duplicate copies of the DLL.
- **Fuel controls are unavailable:** Start or load a flight as the host. Fuel editing becomes available after the aircraft initializes.
- **Reporting an issue:** Open a [GitHub issue](https://github.com/Voraxi/MaydayEngineBoost/issues) with the demo build, mod version, BepInEx version, and the relevant log lines.

## Uninstall or restore four slots

Empty slots 5 and 6, then close the game. To uninstall, remove `MaydayEngineBoost.dll` from `BepInEx\plugins`. To keep thrust and fuel controls with the stock hotbar, set `SlotCount = 4` under `[Inventory]` in `BepInEx\config\local.mayday.engineboost.cfg`, then relaunch.

## Build from source

Use a .NET SDK with .NET 6 targeting support. Run the demo with BepInEx first so it generates the interop assemblies, then run:

```powershell
dotnet build -c Release "-p:MaydayGameDir=C:\path\to\Mayday Airways Demo"
```

The DLL builds to `bin\Release\net6.0`. The project references assemblies from your local demo and BepInEx installation.
