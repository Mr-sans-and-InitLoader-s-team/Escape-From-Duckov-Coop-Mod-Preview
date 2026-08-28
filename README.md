<div align="center">

# 🦆 逃离鸭科夫联机模组 · 正式版

**让《逃离鸭科夫》的单人冒险变成可以和朋友一起探索、战斗与撤离的合作体验。**

[![Version](https://img.shields.io/badge/Version-1.3.6-2ea44f?style=flat-square)](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282)
[![Steam Workshop](https://img.shields.io/badge/Steam-创意工坊-1b2838?style=flat-square&logo=steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282)
[![Stars](https://img.shields.io/github/stars/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview?style=flat-square&logo=github)](https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview/stargazers)
[![Forks](https://img.shields.io/github/forks/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview?style=flat-square&logo=github)](https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview/network/members)
[![Issues](https://img.shields.io/github/issues/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview?style=flat-square&logo=github)](https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview/issues)
[![Last Commit](https://img.shields.io/github/last-commit/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview?style=flat-square&logo=github)](https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview/commits/master)
[![License](https://img.shields.io/badge/License-Modified%20AGPL--3.0-blue?style=flat-square)](LICENSE.txt)

**[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282)** · **[更新记录](CHANGELOG.md)** · **[问题反馈](https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview/issues)**

[English](README_EN.md) | **简体中文**

</div>

> [!IMPORTANT]
> 当前发布版本为正式版 **v1.3.6**。参与联机的玩家应安装相同版本；更新或测试前建议备份重要存档。

## 📖 项目简介

**Escape From Duckov Coop Mod** 是为《逃离鸭科夫》（Escape From Duckov）开发的联机合作模组。项目专注于同步玩家、AI、战斗、场景交互与战利品等核心游戏状态，让原本的单人流程能够与朋友共同体验。

| 项目 | 当前信息 |
| --- | --- |
| 模组版本 | **v1.3.6** |
| 发布状态 | **正式版** |
| 发布渠道 | [Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282) |
| 创意工坊 ID | `3591341282` |
| 联机方式 | 局域网 / 在线联机 |
| 构建目标 | .NET Standard 2.1 |

## ✨ 功能概览

| 模块 | 已支持内容 |
| --- | --- |
| 👥 玩家同步 | 位置、动作、外观、装备、血量与玩家名称 |
| 🤖 AI 同步 | 敌人生成、移动、状态、受伤、死亡与血条 |
| ⚔️ 战斗同步 | 枪械、近战、投掷物、伤害与属性效果 |
| 📦 战利品同步 | 掉落物、战利品箱、拾取与场景容器 |
| 🗺️ 场景同步 | 门、可破坏物、付费交互、抽奖装置与挑战触发器 |
| 🚙 载具同步 | 载具状态、乘坐、控制权与加油状态 |
| 🎮 联机流程 | 房间版本校验、地图切换投票、重连与死亡观战 |
| 🖥️ 联机界面 | 房间管理、同步设置、状态提示与主题界面 |

## 🚀 快速开始

普通玩家无需下载源码或手动编译：

1. 在 [Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282) 订阅模组。
2. 启动游戏，并在模组管理界面中启用本模组。
3. 确认所有参与联机的玩家使用相同版本。
4. 由一名玩家创建联机房间，其他玩家加入后即可开始游戏。

需要自行构建或参与开发时，请继续阅读下方的编译指南。

## 🛠️ 编译指南

### 前置要求

- Visual Studio 2019 或更高版本
- 支持 .NET Standard 2.1 的 .NET SDK
- 已安装《逃离鸭科夫》

### 1. 配置游戏路径

首次编译前，需要设置游戏根目录环境变量 `DUCKOV_GAME_DIRECTORY`。

#### 自动配置（推荐）

1. 双击项目根目录中的 `SetEnvVars_Permanent.bat`。
2. 按提示输入游戏文件夹的完整路径，例如：

   ```text
   C:\Steam\steamapps\common\Escape from Duckov
   ```

3. 脚本会写入当前用户的永久环境变量。
4. 完全关闭并重新打开 Visual Studio 或命令行，使变量生效。

#### 手动配置

在 Windows 的“环境变量”设置中新增用户变量：

```text
变量名：DUCKOV_GAME_DIRECTORY
变量值：游戏根目录的完整路径
```

### 2. 准备依赖

确认 `Shared` 文件夹中包含以下依赖：

- `0Harmony.dll`
- `LiteNetLib.dll`

游戏自身的程序集会根据 `DUCKOV_GAME_DIRECTORY` 自动从 `Duckov_Data\Managed` 目录引用。

### 3. 编译项目

1. 打开 `EscapeFromDuckovCoopMod.sln`。
2. 选择 `Release` 配置。
3. 生成解决方案。

编译输出位于 `EscapeFromDuckovCoopMod/bin/Release/`。

### 常见问题

<details>
<summary><strong>编译时提示找不到游戏 DLL</strong></summary>

确认 `DUCKOV_GAME_DIRECTORY` 指向游戏根目录，而不是 `Duckov_Data` 或 `Managed` 子目录；设置后需要重新打开 Visual Studio。

</details>

<details>
<summary><strong>如何确认环境变量已经生效</strong></summary>

在新的命令提示符窗口中执行：

```bat
echo %DUCKOV_GAME_DIRECTORY%
```

</details>

<details>
<summary><strong>游戏路径中包含空格或括号</strong></summary>

配置脚本支持包含空格和括号的路径，例如 `Program Files (x86)`，直接输入完整路径即可。

</details>

## 🤝 参与与反馈

欢迎通过 [Issues](https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview/issues) 报告问题，或在 [Discussions](https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview/discussions) 中交流建议。

提交问题时，建议附上以下信息：

- 模组版本与游戏版本
- 主机或客户端身份
- 问题复现步骤
- 相关日志、截图或视频

## 💡 致谢

特别感谢以下开发者和测试者对项目的支持：

- **Neko17** — 核心开发
- **Prototype-alpha** — 功能开发与优化
- **所有参与调试与联机测试的朋友们**

本项目使用了以下开源项目：

- [HarmonyLib](https://github.com/pardeike/Harmony) — 运行时代码修改框架
- [LiteNetLib](https://github.com/RevenantX/LiteNetLib) — UDP 网络库

## 📄 许可证

本项目使用基于 **AGPL-3.0 修改的协议**发布。使用或分发衍生作品前，请完整阅读许可证文件：

- [LICENSE.txt](LICENSE.txt) — 完整许可证文本
- [LICENSE_RESTRICTIONS.txt](LICENSE_RESTRICTIONS.txt) — 额外限制说明

主要要求包括：

- ❌ 禁止商业用途
- ❌ 禁止私有服务器闭源使用
- ✅ 必须署名原作者

---

<div align="center">

**如果这个项目对你有帮助，欢迎点亮一个 ⭐ Star。**

</div>
