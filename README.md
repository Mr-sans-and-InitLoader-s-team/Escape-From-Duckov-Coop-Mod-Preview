# 🦆 逃离鸭科夫联机模组先遣版

[![License](https://img.shields.io/badge/License-Modified%20AGPL--3.0-blue.svg)](LICENSE.txt)
[![Steam Workshop](https://img.shields.io/badge/Steam-Workshop-blue.svg)](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282)

**[English](README_EN.md)** | 简体中文

---

## 📖 简介

**Escape From Duckov Coop Mod Preview** 是一个为游戏《逃离鸭科夫》(Escape From Duckov) 开发的联机合作模组。

该项目的目标是让玩家能够在原本的单人游戏中实现稳定的局域网/联机合作游戏体验，包括：

- 🎮 多人游戏同步
- 🤖 AI 行为同步
- 📦 战利品共享
- 👻 死亡观战模式
- ⚔️ 完整的战斗同步
- 🌐 局域网/在线联机支持

---

## 🎯 使用方法

### 普通玩家

**无需手动安装或构建本项目。**

直接通过 Steam 创意工坊订阅即可使用：

👉 **[Steam 创意工坊链接](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282)**

订阅后，启动游戏并启用该模组，即可体验联机功能。

### 开发者

如果你想从源码构建或参与开发，请参阅 [编译指南](#-编译指南)。

---

## 🛠️ 编译指南

### 前置要求

- Visual Studio 2019 或更高版本
- .NET Framework 4.8
- 游戏《逃离鸭科夫》已安装

### 步骤 1：配置环境变量

在首次编译前，你需要设置游戏路径环境变量。

#### 方法一：使用自动配置脚本（推荐）

1. 找到项目根目录下的 `SetEnvVars_Permanent.bat` 文件
2. 双击运行该脚本
3. 按提示输入你的游戏文件夹路径

   **示例路径**：
   ```
   C:\Steam\steamapps\common\Escape from Duckov
   ```

4. 脚本会自动设置环境变量 `DUCKOV_GAME_DIRECTORY`
5. **重要**：完全关闭 Visual Studio 后重新打开，以加载新的环境变量

#### 方法二：手动配置环境变量

1. 右键点击"此电脑" → "属性" → "高级系统设置" → "环境变量"
2. 在"用户变量"区域点击"新建"
3. 变量名：`DUCKOV_GAME_DIRECTORY`
4. 变量值：你的游戏 Managed 文件夹完整路径
5. 点击"确定"保存

### 步骤 2：准备依赖文件

确保 `Shared` 文件夹中包含以下 DLL 文件：

- `0Harmony.dll`
- `LiteNetLib.dll`

### 步骤 3：编译项目

1. 打开 `EscapeFromDuckovCoopMod.sln` 解决方案
2. 选择 `Release` 配置
3. 右键点击解决方案 → "生成解决方案"

编译成功后，输出文件位于 `EscapeFromDuckovCoopMod/bin/Release/` 目录。

### 常见问题

**Q: 编译时提示找不到引用的 DLL？**

A: 确保你已正确设置 `DUCKOV_GAME_DIRECTORY` 环境变量，并且已重启 Visual Studio。

**Q: 环境变量设置后仍然无效？**

A:

1. 在命令行输入 `echo %DUCKOV_GAME_DIRECTORY%` 验证环境变量是否设置成功
2. 确保完全关闭 Visual Studio（包括后台进程）后重新打开

**Q: 路径中包含空格或特殊字符怎么办？**

A: 脚本已支持包含空格和括号的路径，例如 `Program Files (x86)`。直接输入完整路径即可。

---

## 🎯 功能特性

### 核心功能

- ✅ 玩家位置、动作、装备同步
- ✅ AI 敌人状态同步
- ✅ 战利品箱同步
- ✅ 门、可破坏物体同步
- ✅ 投掷物（手雷等）同步
- ✅ 伤害计算与同步
- ✅ 死亡观战模式

### 网络特性

- 🌐 支持局域网联机
- 🌐 支持互联网联机
- ⚡ 优化的网络性能
- 🔄 自动重连机制

---

## 💡 致谢

特别感谢以下开发者对本项目的支持与贡献：

- **Neko17** - 核心开发
- **Prototype-alpha** - 功能开发与优化
- **所有参与 Debug 和测试的朋友们**

感谢以下开源项目：

- [HarmonyLib](https://github.com/pardeike/Harmony) - 运行时代码修改框架
- [LiteNetLib](https://github.com/RevenantX/LiteNetLib) - UDP 网络库

---

## 📄 许可证

本项目使用基于 **AGPL-3.0 修改的协议**发布。

使用本项目的任何衍生作品必须遵守以下条款：

- ❌ **禁止商业用途**
- ❌ **禁止私有服务器闭源使用**
- ✅ **必须署名原作者**

详情请参阅：

- [LICENSE.txt](LICENSE.txt) - 完整许可证文本
- [LICENSE_RESTRICTIONS.txt](LICENSE_RESTRICTIONS.txt) - 额外限制说明

---

## 📞 联系与反馈

欢迎在 [Issues](../../issues) 或 [Discussions](../../discussions) 中提出建议与问题。

本项目仍处于预览阶段，期待社区的参与与反馈！

---

## 🗺️ 项目路线图

- [ ] 更多游戏机制同步
- [ ] 性能优化
- [ ] 更好的错误处理
- [ ] 完善的文档

---

**⭐ 如果这个项目对你有帮助，请给我们一个 Star！**
