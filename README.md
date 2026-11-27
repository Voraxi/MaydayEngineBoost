# Mayday Flight Controls

[简体中文](README.zh-CN.md) · [Download the latest version](https://github.com/Voraxi/MaydayEngineBoost/releases/latest)

A mod for the **Mayday Airways Demo on Steam for Windows**. Adjust thrust and fuel, lock your fuel and watch live flight data from an animated in-game panel, and carry two extra items when you host.

## What it adds

| Feature | How to use it |
| --- | --- |
| **Engine thrust** | Press **F7** and move the thrust slider from **10% to 600%**. Select **Reset to stock** or press **Home** to return to 100%. |
| **Fuel editor** | In a loaded flight you host, press **F7**, set the **Main tanks** or **Reserve** level, then select **Apply fuel**. You can add or remove fuel. Editing the main tanks sets both wing tanks to the same percentage. |
| **Fuel lock** | Turn on **Fuel lock** in the Fuel section and your fuel won't drop in flights you host. Refuelling above the locked level raises the lock. The setting is remembered between sessions. |
| **Flight readout** | The bottom of the panel shows live speed (m/s and knots), altitude above ground, fuel burn per minute and estimated time left on your current fuel. |
| **Six-slot inventory** | In solo play or when you host, press **5** or **6** to select the extra slots. The scroll wheel cycles through slots 1–4. |
| **English and Simplified Chinese** | Both languages are included in one download. The panel follows the game language by default and uses English for other languages. |

In co-op, the host uses the panel and gets the extra inventory slots. The controls and fuel synchronization were tested in a separate copy of the demo. Live Steam co-op is still untested.

## Install

1. In your Steam Library, right-click **Mayday Airways Demo** and select **Manage → Browse local files**.
2. If BepInEx is already working for this demo, skip to step 4. Otherwise, follow the [BepInEx guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html) and download **BepInEx 6 for Unity IL2CPP, Windows x64**. Extract its contents into the folder that contains `MaydayAirways.exe`.
3. Start the demo through Steam, wait for the main menu, then close it. This gives BepInEx time to set up its folders.
4. Open the [latest GitHub release](https://github.com/Voraxi/MaydayEngineBoost/releases/latest) and download **MaydayEngineBoost.zip** under **Assets**. Extract it and copy `MaydayEngineBoost.dll` into the game's `BepInEx\plugins` folder.
5. Launch the demo through Steam and press **F7**.

**Updating?** Close the game, download the latest ZIP, and replace the DLL in `BepInEx\plugins`.

## Change the panel language

After launching the mod once, close the game and open `BepInEx\config\local.mayday.engineboost.cfg`. Under `[Interface]`, set `Language = Auto`, `Language = English`, or `Language = Chinese`. Save and relaunch.

## Troubleshooting

- **F7 does nothing.** Check that you extracted the mod ZIP and copied the DLL to `BepInEx\plugins`. Confirm you installed the Unity IL2CPP Windows x64 version of BepInEx. Check `BepInEx\LogOutput.log` to see whether the mod loaded.
- **Fuel sliders are unavailable.** Start or load a flight as the host, then reopen the panel.
- **Found a problem?** Report it in [GitHub Issues](https://github.com/Voraxi/MaydayEngineBoost/issues) with what you were doing and the relevant lines from `BepInEx\LogOutput.log`.

## Uninstall or return to four slots

Empty slots 5 and 6 and close the game. Remove `MaydayEngineBoost.dll` from `BepInEx\plugins` to uninstall. To keep the mod and return to four slots, set `SlotCount = 4` under `[Inventory]` in the config file and relaunch.

<details>
<summary>Build from source</summary>

Use a .NET SDK with .NET 6 targeting support. Launch the demo once with BepInEx before building, then point the command at your demo folder.

```powershell
dotnet build -c Release "-p:MaydayGameDir=C:\path\to\Mayday Airways Demo"
```

Find the DLL under `bin\Release\net6.0`.

</details>
