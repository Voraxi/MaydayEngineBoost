# Mayday Engine Boost

Adjust forward engine thrust in the Mayday Airways Demo for Windows. Press **F7** for the in-game slider. The lobby host applies the setting to the aircraft in co-op.

## Install

1. Install [BepInEx 6 for Unity IL2CPP](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html) and launch the demo once.
2. Open the demo folder via **Steam → Manage → Browse local files**.
3. Copy `MaydayEngineBoost.dll` from the download into `BepInEx\plugins` on the host's PC, then launch through Steam.

Built for Steam demo build 26546546 and BepInEx 6.0.0-be.793.

## Controls

- **F7** opens and closes the panel. The large readout shows your current thrust and how far it is from stock.
- Drag the slider, click **−** or **+**, or use **←** and **→** to adjust thrust from 10% to 500% in five-point steps. The default is 125%.
- Use **Reset to stock** or press **Home** to restore 100%. The amber marker shows the stock setting on the slider.
- To unload the mod entirely, close the game and move the DLL out of `BepInEx\plugins`. You can also set `Enabled = false` in `BepInEx\config\local.mayday.engineboost.cfg` before launching.

## Build from source

Use a .NET SDK that can target .NET 6. Run the demo with BepInEx at least once so it generates the interop assemblies, then run:

```powershell
dotnet build -c Release "-p:MaydayGameDir=C:\path\to\Mayday Airways Demo"
```

The DLL builds to `bin\Release\net6.0`. The project references the assemblies in your own installation.

