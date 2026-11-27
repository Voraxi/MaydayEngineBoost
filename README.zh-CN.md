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

一个压缩包包含全部所需文件，包括 BepInEx 模组加载器。

1. 打开 [最新版本页面](https://github.com/Voraxi/MaydayEngineBoost/releases/latest)，在 **Assets** 下下载 **MaydayFlightControls-Full.zip**。
2. 在 Steam 库中右键点击 **Mayday Airways Demo**，依次选择 **管理 → 浏览本地文件**，打开游戏文件夹（即 `MaydayAirways.exe` 所在的文件夹）。
3. 打开压缩包，全选里面的所有内容（**Ctrl+A**），全部拖进游戏文件夹。如果 Windows 询问是否替换文件，选择 **替换**。
4. 从 Steam 启动试玩版，按 **F7**。首次启动时 BepInEx 需要准备文件，会比平时多等一两分钟。

**更新模组或已装有 BepInEx**：关闭游戏，下载 **MaydayEngineBoost.zip**（仅模组），把 `MaydayEngineBoost.dll` 复制到游戏目录下的 `BepInEx\plugins` 并替换旧文件。

完整安装包内含官方原版 [BepInEx](https://github.com/BepInEx/BepInEx) 6.0.0-be.793（Unity IL2CPP，Windows x64），未作修改，采用 LGPL-2.1 许可证。

## 切换面板语言

先启动一次装有模组的游戏，再退出。打开 `BepInEx\config\local.mayday.engineboost.cfg`，在 `[Interface]` 下修改 `Language`。填 `Auto` 跟随游戏，填 `Chinese` 强制显示简体中文，填 `English` 强制显示英文。保存文件后重启游戏。

## 常见问题

- **按 F7 没反应。** 确认 `winhttp.dll` 和 `BepInEx` 文件夹直接位于 `MaydayAirways.exe` 旁边（不是在多出来的子文件夹里），并且 `MaydayEngineBoost.dll` 在 `BepInEx\plugins` 下。然后在 `BepInEx\LogOutput.log` 中搜索 `Mayday Flight Controls`，查看模组是否加载。
- **油量滑块不可用。** 以房主身份开始或载入一个航班，再打开面板。
- **发现问题。** 可以在 [GitHub Issues](https://github.com/Voraxi/MaydayEngineBoost/issues) 说明操作步骤，并附上 `BepInEx\LogOutput.log` 中相关的几行。

## 卸载或恢复四格物品栏

先取出第 5 和第 6 格里的物品，再退出游戏。要卸载模组，从 `BepInEx\plugins` 移除 `MaydayEngineBoost.dll`。如果还要卸载 BepInEx，删除 `BepInEx` 和 `dotnet` 文件夹、`winhttp.dll`、`doorstop_config.ini` 和 `.doorstop_version`；这样其他 BepInEx 模组也会失效。要保留模组并恢复四格物品栏，在配置文件的 `[Inventory]` 下设置 `SlotCount = 4`，然后重启游戏。

如需从源码构建，请展开[英文说明](README.md)底部的 **Build from source**。
