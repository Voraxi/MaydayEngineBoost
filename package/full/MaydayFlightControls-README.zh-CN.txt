Mayday 飞行控制 1.9.0 - 完整安装包
适用于 Steam 上的 Mayday Airways Demo（Windows 版）。
English instructions: MaydayFlightControls-README.txt

本安装包已包含模组所需的全部文件，包括 BepInEx。

安装（约一分钟）
1. 在 Steam 库中右键点击 Mayday Airways Demo，选择“管理 > 浏览本地文件”，
   打开游戏文件夹（即 MaydayAirways.exe 所在的文件夹）。
2. 打开本压缩包，全选里面的所有内容（Ctrl+A），全部拖进游戏文件夹。
   如果 Windows 询问是否替换文件，选择“替换”。
3. 从 Steam 启动游戏。首次启动时 BepInEx 需要准备文件，会比平时多等
   一两分钟，只有第一次会这样。
4. 在主菜单或航班中按 F7。

完成，无需再下载其他文件。

压缩包内容
  BepInEx\plugins\MaydayEngineBoost.dll   模组本体
  BepInEx\core、dotnet、winhttp.dll、
  doorstop_config.ini、.doorstop_version   BepInEx 6.0.0-be.793（模组加载器）
  BepInEx-LICENSE.txt                      BepInEx 许可证（LGPL-2.1）

操作
- 按 F7 打开或关闭面板。
- 推力：拖动滑块、点击 - 和 +，或按左右方向键。点击“恢复原版”或按 Home 恢复 100%。
- 燃油（当房主的航班中）：拖动主油箱或备用油箱的油量条，再点击“应用油量”。
  打开“燃油锁定”后油量不会下降。
- 乘客（单人或当房主时）：选择目的地前，用 - 或 + 调整人数，
  “满员”按已安装座位数安排，“默认”恢复游戏分配的人数。
- 飞行数据：显示速度、离地高度、油耗和剩余续航。
- 单人游玩或当房主时，按 5 或 6 使用额外的两个物品格。

以后更新
在发布页下载 MaydayEngineBoost.zip，替换 BepInEx\plugins\MaydayEngineBoost.dll
即可，无需重新安装 BepInEx。

卸载
- 只卸载本模组：删除 BepInEx\plugins\MaydayEngineBoost.dll。
- 全部卸载：删除 BepInEx 和 dotnet 文件夹、winhttp.dll、doorstop_config.ini
  和 .doorstop_version。删除 BepInEx 后，其他 BepInEx 模组也会失效。

常见问题
- 按 F7 没反应：确认 winhttp.dll 和 MaydayAirways.exe 在同一个文件夹里
  （不是在子文件夹里），然后在 BepInEx\LogOutput.log 中搜索
  “Mayday Flight Controls”。
- 反馈问题：https://github.com/Voraxi/MaydayEngineBoost/issues
