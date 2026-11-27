# Mayday 飞行控制

[English](README.md) · [下载最新版](https://github.com/Voraxi/MaydayEngineBoost/releases/latest)

适用于 Steam 上的 **Mayday Airways Demo（Windows 版）**。通过带动画的游戏内面板调整推力和油量、锁定燃油并查看实时飞行数据，单人游玩或当房主时还能多带两件物品。

## 功能一览

| 功能 | 用法 |
| --- | --- |
| **发动机推力** | 按 **F7** 打开面板，滑块可将推力调到 **10% 至 600%**。点击**恢复原版**或按 **Home** 恢复到 100%。 |
| **油量编辑** | 当房主并进入航班后，按 **F7** 设置主油箱或备用油箱的油量，再点击**应用油量**。可以加油，也可以减少油量。调整主油箱时，左右两侧会设为相同的油量百分比。 |
| **燃油锁定** | 在燃油区域打开**燃油锁定**后，你当房主的航班油量不会下降。加油超过锁定水平时，锁定水平会随之提高。该设置会被保存。 |
| **飞行数据** | 面板底部实时显示速度（米/秒和节）、离地高度、每分钟油耗，以及按当前油量估算的剩余续航时间。 |
| **六格物品栏** | 单人游玩或当房主时，按 **5** 或 **6** 选择额外的物品格。鼠标滚轮在第 1 至 4 格之间切换。 |
| **简体中文和英文** | 同一个下载包包含两种语言。面板默认跟随游戏语言，其他语言下显示英文。 |

联机时由房主安装和操作模组，额外的物品栏格子也归房主角色使用。面板操作和油量同步已在独立的试玩版副本中测试，暂未验证 Steam 实际联机。

## 安装步骤

1. 在 Steam 库中右键点击 **Mayday Airways Demo**，依次选择 **管理 → 浏览本地文件**。
2. 如果这个试玩版已经装好并能使用 BepInEx，直接看第 4 步。否则，按 [BepInEx 安装指南](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html) 下载 **BepInEx 6 的 Unity IL2CPP Windows x64 版**，将解压后的文件放进 `MaydayAirways.exe` 所在的文件夹。
3. 从 Steam 启动试玩版，等主菜单出现后退出，让 BepInEx 生成所需文件夹。
4. 打开 [GitHub 最新版本页面](https://github.com/Voraxi/MaydayEngineBoost/releases/latest)，在 **Assets** 下下载 **MaydayEngineBoost.zip**。解压后，把 `MaydayEngineBoost.dll` 复制到游戏目录下的 `BepInEx\plugins` 文件夹。
5. 从 Steam 重新启动游戏，按 **F7** 打开模组面板。

**更新模组**：关闭游戏，下载最新版压缩包，解压并替换 `BepInEx\plugins` 里的旧 DLL。

## 切换面板语言

先启动一次装有模组的游戏，再退出。打开 `BepInEx\config\local.mayday.engineboost.cfg`，在 `[Interface]` 下修改 `Language`。填 `Auto` 跟随游戏，填 `Chinese` 强制显示简体中文，填 `English` 强制显示英文。保存文件后重启游戏。

## 常见问题

- **按 F7 没反应。** 确认已解压模组，并把 DLL 放在 `BepInEx\plugins` 下。检查 BepInEx 是否为 Unity IL2CPP Windows x64 版。打开 `BepInEx\LogOutput.log` 可以查看模组是否加载。
- **油量滑块不可用。** 以房主身份开始或载入一个航班，再打开面板。
- **发现问题。** 可以在 [GitHub Issues](https://github.com/Voraxi/MaydayEngineBoost/issues) 说明操作步骤，并附上 `BepInEx\LogOutput.log` 中相关的几行。

## 卸载或恢复四格物品栏

先取出第 5 和第 6 格里的物品，再退出游戏。要卸载，从 `BepInEx\plugins` 移除 `MaydayEngineBoost.dll`。要保留模组并恢复四格物品栏，在配置文件的 `[Inventory]` 下设置 `SlotCount = 4`，然后重启游戏。

如需从源码构建，请展开[英文说明](README.md)底部的 **Build from source**。
