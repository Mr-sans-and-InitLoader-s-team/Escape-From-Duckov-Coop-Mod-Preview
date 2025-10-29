# 🦆 Escape From Duckov Coop Mod Preview / 鸭科夫联机模组先遣版

Escape From Duckov Coop Mod Preview 是一个为游戏 Escape From Duckov（逃离鸭科夫） 开发的 联机模组（Co-op Mod）。

该项目的目标是让玩家能够在原本的单人游戏中实现稳定的局域网/联机合作游戏体验，包括玩家同步、AI 交互、战利品共享、死亡观战等核心功能。

---

Escape From Duckov Coop Mod Preview is a multiplayer co-op mod developed for Escape From Duckov.

This mod is to enable stable LAN and online co-op gameplay in a game originally designed for single-player, featuring player synchronization, AI behavior sync, loot sharing, death spectator mode, and more.

## 使用方法 / How to Use

### 使用预构建的mod文件

直接通过 Steam 创意工坊订阅即可使用：

[Steam 创意工坊链接](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282)

订阅后，启动游戏并启用该模组，即可体验联机功能。

---

No manual installation or build is required.

Simply subscribe on Steam Workshop:

[Steam Workshop Page](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282)

Launch the game and enable the mod to start playing cooperatively.

### 手动构建

#### 编译

1. 将 `Escape from Duckov\Duckov_Data\` 中的 `Managed` 文件夹中的内容复制到项目的 `Managed` 文件夹中

2. 使用 Visual Studio 或其他 IDE 打开项目并编译

3. 从 `鸭科夫联机Mod\bin\Debug` 或 `鸭科夫联机Mod\bin\Release` 中获取输出文件 `0Harmony.dll` `LiteNetLib.dll` `鸭科夫联机Mod.dll`

#### 手动安装

1. 在 `Escape from Duckov\Duckov_Data` 文件夹中创建 `Mods` 文件夹

2. 在 `Escape from Duckov\Duckov_Data\Mods` 文件夹中创建 `鸭科夫联机Mod` 文件夹

3. 将输出文件 `0Harmony.dll` `LiteNetLib.dll` `鸭科夫联机Mod.dll` 放入之前创建的 `鸭科夫联机Mod` 文件夹中

4. 在 `鸭科夫联机Mod` 文件夹中创建 `info.ini` 与 `preview.png`

5. 进入游戏在 `Mod` 菜单中启用模组

##### 文件说明

```
Mods/
└── 鸭科夫联机Mod/
    ├── 0Harmony.dll -- hook库 (注: 在创意工坊版本中此库是作为前置模组独立分发的，这里放一起只是为了开发方便，如之前已安装过前置就不要放这个文件)
    ├── LiteNetLib.dll -- 网络库 (注: 在创意工坊版本中此库是作为前置模组独立分发的，这里放一起只是为了开发方便，如之前已安装过前置就不要放这个文件)
    ├── 鸭科夫联机Mod.dll -- 模组本体
    ├── info.ini -- 模组描述文件
    └── preview.png -- 模组图标
```

##### 文件内容

info.ini: 
```
name = 鸭科夫联机Mod
displayName = 鸭科夫联机Mod
description = 用于局域网联机

; 模组创意工坊id 这里随便写的
publishedFileId = 114514
``` 

preview.png:

任意256 * 256的图片

## 致谢 / Credits

特别感谢以下开发者对本项目的支持与贡献：

Special thanks to the following contributors:

-  Neko17
-  Prototype-alpha
-  所有参与 Debug 和测试的朋友

感谢下面的开源项目：

Special thanks to the following Open Source projects:

-  HarmonyLib
-  LiteNetLib

## 许可证 / License

本项目使用一个基于 AGPL-3.0 修改的协议发布。

使用本项目的任何衍生作品必须遵守以下条款：

-   禁止商业用途
-   禁止私有服务器闭源使用
-   必须署名原作者

---

This project is released under a modified AGPL-3.0 License.

All derived works must comply with the following terms:

-   No commercial use allowed
-   No closed-source server deployment
-   Attribution required

## 联系与反馈 / Contact & Feedback

欢迎在 Issues 或 Discussions 中提出建议与问题。

本项目仍处于预览阶段，期待社区的参与与反馈！

---

Feel free to report bugs or share suggestions through Issues or Discussions.

This project is still in preview — community contributions are highly appreciated!
