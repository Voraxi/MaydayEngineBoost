MAYDAY FLIGHT CONTROLS 1.9.0

For the Mayday Airways Demo on Steam for Windows.
For Simplified Chinese instructions, open README.zh-CN.txt.

INSTALL
1. In Steam, right-click Mayday Airways Demo and choose
   Manage > Browse local files.
2. If BepInEx is already working for the demo, skip to step 4.
   Otherwise, download BepInEx 6 for Unity IL2CPP, Windows x64.
   Extract it into the folder that contains MaydayAirways.exe.
3. Start the demo through Steam, wait for the main menu, then close it.
4. Copy MaydayEngineBoost.dll from this ZIP to BepInEx\plugins.
   If you are updating, replace the old DLL.
5. Start the demo and press F7.

BepInEx guide:
https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html

CONTROLS
- Press F7 to open or close the panel. Adjust engine thrust from 10% to 600%
  with the slider, the - and + buttons, or the Left and Right arrow keys.
  Reset to stock or Home returns it to 100%.
- In a loaded flight you host, drag the Main tanks or Reserve gauge and select
  Apply fuel. Editing Main tanks sets both wing tanks to the same percentage.
- Turn on Fuel lock and your fuel won't drop in flights you host. Refuelling
  above the locked level raises the lock. The setting is remembered.
- Before choosing a destination in solo play or as host, set the passenger
  count with - and +. Max fills all installed seats and Default restores the
  game's count. The setting is remembered.
- The Flight section shows speed, altitude above ground, fuel burn per minute
  and estimated time left on your current fuel.
- Press 5 or 6 for the extra inventory slots in solo play or when you host.
  The scroll wheel cycles through slots 1–4.

LANGUAGE
One DLL includes English and Simplified Chinese. The panel follows the
game language by default. After launching the mod once, close the game
and open BepInEx\config\local.mayday.engineboost.cfg.
Under [Interface], set Language to Auto, English, or Chinese, then relaunch.

For co-op, install the mod on the host's PC. Live Steam co-op is untested.

For troubleshooting and uninstall steps, see:
https://github.com/Voraxi/MaydayEngineBoost
