MAYDAY FLIGHT CONTROLS 1.9.0 - FULL PACKAGE
For the Mayday Airways Demo on Steam for Windows.
简体中文说明请见 MaydayFlightControls-README.zh-CN.txt。

This package already contains everything the mod needs, including BepInEx.

INSTALL (about one minute)
1. In Steam, right-click Mayday Airways Demo and choose
   Manage > Browse local files. The game folder opens.
   (It is the folder that contains MaydayAirways.exe.)
2. Open this ZIP, select everything inside it (Ctrl+A),
   and drag it all into that game folder.
   If Windows asks about replacing files, choose "Replace".
3. Start the game through Steam. The first start takes a minute or two
   longer than usual while BepInEx prepares itself. This happens once.
4. At the main menu or in a flight, press F7.

That's it. Nothing else to download.

WHAT'S IN THIS ZIP
  BepInEx\plugins\MaydayEngineBoost.dll   the mod
  BepInEx\core, dotnet, winhttp.dll,
  doorstop_config.ini, .doorstop_version   BepInEx 6.0.0-be.793 (mod loader)
  BepInEx-LICENSE.txt                      BepInEx license (LGPL-2.1)

CONTROLS
- F7 opens or closes the panel.
- Thrust: slider, - and + buttons, or the Left and Right arrow keys.
  Reset to stock or Home returns it to 100%.
- Fuel (in a flight you host): drag the Main tanks or Reserve gauge,
  then select Apply fuel. Turn on Fuel lock to stop fuel from dropping.
- Passengers (solo or as host): before choosing a destination, use - or +,
  Max (all installed seats) or Default (the game's count).
- Flight: shows speed, altitude, fuel burn and time left.
- Press 5 or 6 for two extra inventory slots in solo play or when you host.

UPDATING LATER
Download MaydayEngineBoost.zip from the releases page and replace
BepInEx\plugins\MaydayEngineBoost.dll. You don't need BepInEx again.

UNINSTALL
- Remove only the mod: delete BepInEx\plugins\MaydayEngineBoost.dll.
- Remove everything: delete the BepInEx and dotnet folders,
  winhttp.dll, doorstop_config.ini and .doorstop_version.
  Other BepInEx mods stop working if you remove BepInEx.

TROUBLESHOOTING
- F7 does nothing: make sure winhttp.dll sits right next to
  MaydayAirways.exe (not in a subfolder). Then check
  BepInEx\LogOutput.log for "Mayday Flight Controls".
- Help: https://github.com/Voraxi/MaydayEngineBoost/issues
