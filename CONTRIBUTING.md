# 贡献指南 / Contributing Guidelines

## 如何贡献

感谢您对本项目的关注！我们欢迎所有形式的贡献。

### 报告问题

在提交新Issue前，请先搜索现有Issue，避免重复。

提交Issue时请包含：
- 问题的详细描述
- 复现步骤
- 预期行为和实际行为
- 相关日志或截图
- 您的环境信息（游戏版本、操作系统等）

### 提交代码

1. Fork本仓库
2. 创建您的功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交您的修改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建Pull Request

### 代码规范

- 保持代码风格一致
- 为复杂逻辑添加必要的注释
- 确保代码可以正常编译和运行
- 在PR描述中清楚说明修改内容和原因

### 项目设置

请参考 [SETUP_GUIDE.md](SETUP_GUIDE.md) 了解如何设置开发环境。

## Mod安装说明

安装Mod时需要复制以下文件到游戏Mod目录：

```
游戏目录/Duckov_Data/Mods/你的Mod文件夹/
├── 鸭科夫联机Mod.dll
├── LiteNetLib.dll
└── 0Harmony.dll
```

这些文件可以在 `鸭科夫联机Mod/bin/Release/` 或 `鸭科夫联机Mod/bin/x64/Release/` 中找到。

## 依赖项说明

本项目依赖以下库：
- **Lib.Harmony 2.4.1** - 用于运行时方法hook
- **LiteNetLib 1.1.0** - 网络通信库
- **Steamworks.NET** - Steam集成（来自游戏目录）

所有第三方依赖都必须与Mod主DLL一起部署到游戏Mod目录。

## 构建说明

1. 确保已安装Visual Studio 2019或更高版本
2. 克隆仓库并还原NuGet包
3. 设置游戏路径环境变量或在项目中配置
4. 构建项目（推荐使用Release配置）
5. 将输出目录下的所有DLL文件复制到游戏Mod文件夹

## 联系方式

如有问题，欢迎在Issues区讨论。

---

# Contributing Guidelines (English)

## How to Contribute

Thank you for your interest in this project! We welcome all forms of contributions.

### Reporting Issues

Before submitting a new issue, please search existing issues to avoid duplicates.

When submitting an issue, please include:
- Detailed description of the problem
- Steps to reproduce
- Expected vs actual behavior
- Relevant logs or screenshots
- Your environment (game version, OS, etc.)

### Submitting Code

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Create a Pull Request

### Code Standards

- Maintain consistent code style
- Add necessary comments for complex logic
- Ensure code compiles and runs correctly
- Clearly describe changes and reasons in PR description

### Project Setup

See [SETUP_GUIDE.md](SETUP_GUIDE.md) for development environment setup.

## Mod Installation

To install the mod, copy these files to the game's mod directory:

```
GameDirectory/Duckov_Data/Mods/YourModFolder/
├── 鸭科夫联机Mod.dll
├── LiteNetLib.dll
└── 0Harmony.dll
```

These files can be found in `鸭科夫联机Mod/bin/Release/` or `鸭科夫联机Mod/bin/x64/Release/`.

## Dependencies

This project depends on:
- **Lib.Harmony 2.4.1** - Runtime method hooking
- **LiteNetLib 1.1.0** - Networking library
- **Steamworks.NET** - Steam integration (from game directory)

All third-party dependencies must be deployed to the game mod directory alongside the main DLL.

## Build Instructions

1. Install Visual Studio 2019 or later
2. Clone the repository and restore NuGet packages
3. Set game path environment variable or configure in project
4. Build the project (Release configuration recommended)
5. Copy all DLL files from output directory to game mod folder

## Contact

Feel free to discuss in the Issues section.

