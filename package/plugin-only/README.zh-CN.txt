Mayday 飞行控制 1.9.0

适用于 Steam 上的 Mayday Airways Demo（Windows 版）。
英文说明请见 README.txt。

安装步骤
1. 在 Steam 库中右键点击 Mayday Airways Demo，选择“管理 > 浏览本地文件”。
2. 如果已经装好并能使用 BepInEx，跳到第 4 步。否则下载
   BepInEx 6 的 Unity IL2CPP Windows x64 版，将解压后的文件放到
   MaydayAirways.exe 所在的文件夹。
3. 从 Steam 启动试玩版，等主菜单出现后退出。
4. 将本压缩包里的 MaydayEngineBoost.dll 复制到 BepInEx\plugins。
   如果是更新，替换旧 DLL。
5. 启动游戏，按 F7。

BepInEx 安装指南：
https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html

功能
- 按 F7 打开或关闭面板。用滑块、- 和 + 按钮或左右方向键将发动机推力
  调到 10% 至 600%。点击“恢复原版”或按 Home 恢复 100%。
- 当房主并进入航班后，拖动主油箱或备用油箱的油量条，再点击“应用油量”。
  调整主油箱时，左右两侧会设为相同的油量百分比。
- 打开“燃油锁定”后，你当房主的航班油量不会下降。加油超过锁定水平时，
  锁定水平会随之提高。该设置会被保存。
- 单人游玩或当房主时，选择目的地前用 - 和 + 设定乘客人数。“满员”按已安装
  座位数安排，“默认”恢复游戏分配的人数。该设置会被保存。
- “飞行数据”区域显示速度、离地高度、每分钟油耗和按当前油量估算的剩余续航。
- 单人游玩或当房主时，按 5 或 6 选择额外的物品格。
  鼠标滚轮在第 1 至 4 格之间切换。

切换语言
同一个 DLL 包含简体中文和英文。面板默认跟随游戏语言。
启动一次装有模组的游戏并退出，然后打开
BepInEx\config\local.mayday.engineboost.cfg。
在 [Interface] 下将 Language 设为 Auto（跟随游戏）、Chinese（中文）
或 English（英文），保存后重启游戏。

联机由房主安装，暂未验证 Steam 实际联机。

排查问题和卸载步骤：
https://github.com/Voraxi/MaydayEngineBoost/blob/main/README.zh-CN.md
