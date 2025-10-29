# 鸭科夫联机Mod 开发环境配置指南
# Duckov Coop Mod - Development Setup Guide

---

## 📋 目录 / Table of Contents

1. [前置要求](#前置要求--prerequisites)
2. [快速开始](#快速开始--quick-start)
3. [详细配置步骤](#详细配置步骤--detailed-setup)
4. [常见问题](#常见问题--troubleshooting)
5. [项目结构说明](#项目结构说明--project-structure)

---

## 🎯 前置要求 / Prerequisites

### 必需软件 / Required Software

- **Visual Studio 2019/2022** 或 **JetBrains Rider**
- **.NET Framework 4.8 SDK**
- **Escape from Duckov** 游戏本体（Steam版本）
- **Git** (用于克隆仓库)

### 推荐工具 / Recommended Tools

- **NuGet CLI** (用于包管理)
- **dnSpy** (用于反编译调试)

---

## 🚀 快速开始 / Quick Start

### 步骤 1: 克隆仓库

```bash
git clone https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview.git
cd Escape-From-Duckov-Coop-Mod-Preview
```

### 步骤 2: 配置游戏路径

编辑项目根目录的 `Directory.Build.props` 文件:

```xml
<DuckovGamePath>你的游戏安装路径</DuckovGamePath>
```

**示例路径:**
- `E:\SteamLibrary\steamapps\common\Escape from Duckov`
- `C:\Program Files (x86)\Steam\steamapps\common\Escape from Duckov`
- `D:\Games\Steam\steamapps\common\Escape from Duckov`

### 步骤 3: 准备依赖库

#### 方法A: 使用NuGet自动下载 (推荐)

在项目根目录执行:

```powershell
# 还原NuGet包
nuget restore 鸭科夫联机Mod.sln

# 复制DLL到Shared目录
New-Item -ItemType Directory -Path "Shared" -Force
Copy-Item "packages\Lib.Harmony.2.4.1\lib\net48\0Harmony.dll" "Shared\" -Force
Copy-Item "packages\LiteNetLib.1.1.0\lib\netstandard2.1\LiteNetLib.dll" "Shared\" -Force
```

#### 方法B: 手动复制

1. 下载依赖:
   - [0Harmony.dll](https://github.com/pardeike/Harmony/releases) (v2.3.3+)
   - [LiteNetLib.dll](https://github.com/RevenantX/LiteNetLib/releases) (v1.1.0)

2. 复制到 `Shared\` 目录

### 步骤 4: 编译项目

```bash
# 使用MSBuild编译
msbuild 鸭科夫联机Mod.sln /p:Configuration=Release

# 或在Visual Studio中按F5
```

### 步骤 5: 部署Mod

编译成功后,将 `bin\Release\鸭科夫联机Mod.dll` 复制到游戏Mod目录。

---

## 📖 详细配置步骤 / Detailed Setup

### 1. 理解路径变量系统

项目使用 `Directory.Build.props` 管理所有路径依赖:

```xml
<PropertyGroup>
  <!-- 游戏根目录 -->
  <DuckovGamePath>E:\SteamLibrary\steamapps\common\Escape from Duckov</DuckovGamePath>
  
  <!-- 自动计算的路径 -->
  <DuckovManagedPath>$(DuckovGamePath)\Duckov_Data\Managed</DuckovManagedPath>
  <SharedLibPath>$(MSBuildThisFileDirectory)Shared</SharedLibPath>
</PropertyGroup>
```

**关键概念:**
- `DuckovGamePath`: 游戏安装根目录 (需手动配置)
- `DuckovManagedPath`: Unity Managed程序集目录 (自动推导)
- `SharedLibPath`: 第三方库存放目录 (自动推导)

### 2. 查找游戏安装路径

#### Windows系统

**Steam默认路径:**
```
C:\Program Files (x86)\Steam\steamapps\common\Escape from Duckov
```

**如何查找非默认路径:**

1. 打开Steam客户端
2. 右键点击"Escape from Duckov"
3. 选择 "管理" → "浏览本地文件"
4. 复制地址栏路径

#### 验证路径正确性

确保路径下存在以下结构:

```
Escape from Duckov/
├── Duckov.exe
├── Duckov_Data/
│   └── Managed/
│       ├── UnityEngine.dll
│       ├── TeamSoda.Duckov.Core.dll
│       └── ... (其他DLL)
└── ...
```

### 3. 依赖管理详解

#### 依赖分类

项目依赖分为三类:

| 类别 | 来源 | 示例 |
|------|------|------|
| **系统框架** | .NET Framework | System.dll, System.Collections.dll |
| **第三方库** | NuGet/手动 | 0Harmony.dll, LiteNetLib.dll |
| **游戏程序集** | 游戏Managed目录 | TeamSoda.Duckov.Core.dll, UnityEngine.dll |

#### 第三方库详细配置

**0Harmony.dll**
- **作用**: 运行时代码注入框架(用于Mod功能)
- **版本要求**: >= 2.3.3
- **获取方式**:
  ```bash
  nuget install Lib.Harmony -Version 2.4.1 -OutputDirectory packages
  ```
- **放置位置**: `Shared\0Harmony.dll`

**LiteNetLib.dll**
- **作用**: 轻量级网络库(用于联机功能)
- **版本要求**: 1.1.0
- **获取方式**:
  ```bash
  nuget install LiteNetLib -Version 1.1.0 -OutputDirectory packages
  ```
- **放置位置**: `Shared\LiteNetLib.dll`

### 4. 编译配置

#### 使用Visual Studio

1. 打开 `鸭科夫联机Mod.sln`
2. 检查编译输出窗口中的路径信息:
   ```
   ==================================================
   Duckov Coop Mod - 路径配置
   ==================================================
   游戏目录: E:\SteamLibrary\steamapps\common\Escape from Duckov
   Managed目录: E:\SteamLibrary\...\Managed
   Shared目录: H:\...\Shared
   ==================================================
   ```
3. 如果出现警告,检查对应路径是否存在
4. 按 `Ctrl+Shift+B` 编译

#### 使用命令行

```bash
# Debug版本
msbuild 鸭科夫联机Mod.sln /p:Configuration=Debug

# Release版本
msbuild 鸭科夫联机Mod.sln /p:Configuration=Release

# 指定自定义游戏路径
msbuild 鸭科夫联机Mod.sln /p:DuckovGamePath="D:\Games\Duckov"
```

### 5. 高级配置

#### 多开发者环境配置

创建 `Directory.Build.props.user` (不提交到Git):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <!-- 覆盖默认配置 -->
    <DuckovGamePath>C:\MyCustomPath\Escape from Duckov</DuckovGamePath>
  </PropertyGroup>
</Project>
```

然后修改 `.gitignore`:
```gitignore
Directory.Build.props.user
```

#### CI/CD配置

在CI环境中通过环境变量配置:

```yaml
# GitHub Actions示例
env:
  DuckovGamePath: ${{ secrets.DUCKOV_GAME_PATH }}
```

---

## 🔧 常见问题 / Troubleshooting

### 问题 1: 找不到游戏DLL

**错误信息:**
```
警告: 游戏Managed目录不存在: E:\...\Managed
```

**解决方案:**
1. 检查 `Directory.Build.props` 中的 `DuckovGamePath` 是否正确
2. 验证游戏是否完整安装
3. 在Steam中"验证游戏文件完整性"

### 问题 2: 缺少第三方DLL

**错误信息:**
```
错误 CS0246: 未能找到类型或命名空间名"HarmonyLib"
```

**解决方案:**
1. 检查 `Shared\` 目录是否存在 `0Harmony.dll` 和 `LiteNetLib.dll`
2. 重新执行步骤3的依赖准备
3. 清理并重新编译:
   ```bash
   msbuild /t:Clean
   msbuild /t:Rebuild
   ```

### 问题 3: 编译成功但Mod无法加载

**可能原因:**
- DLL放置位置错误
- 游戏版本与Mod不匹配
- 缺少游戏依赖项

**调试步骤:**
1. 使用dnSpy检查生成的DLL依赖
2. 查看游戏日志文件
3. 确认Mod加载器版本

### 问题 4: 路径包含中文/特殊字符

**建议:**
- 避免使用包含空格的路径
- 使用英文路径更稳定
- 如必须使用中文路径,确保Visual Studio配置了UTF-8编码

### 问题 5: 权限不足

**错误信息:**
```
拒绝访问路径 'E:\...\Duckov_Data\Managed\...'
```

**解决方案:**
- 以管理员身份运行Visual Studio
- 检查文件夹权限设置
- 关闭游戏/Steam后再编译

---

## 📂 项目结构说明 / Project Structure

```
Escape-From-Duckov-Coop-Mod-Preview/
│
├── Directory.Build.props          # 全局路径配置文件 ⭐
├── SETUP_GUIDE.md                 # 本配置指南
├── README.md                      # 项目说明
├── LICENSE.txt                    # 许可证
│
├── Shared/                        # 第三方库目录 ⭐
│   ├── 0Harmony.dll              # (需手动放置)
│   ├── LiteNetLib.dll            # (需手动放置)
│   └── README.md                 # 依赖说明
│
├── 鸭科夫联机Mod/                 # 主项目目录
│   ├── 鸭科夫联机Mod.csproj      # 项目文件 ⭐
│   ├── packages.config            # NuGet配置
│   │
│   ├── Main/                      # 核心代码
│   │   ├── Mod.cs                # Mod入口
│   │   ├── COOPManager.cs        # 联机管理器
│   │   └── HarmonyFix.cs         # Harmony补丁
│   │
│   ├── Net/                       # 网络同步
│   │   ├── NetInterpolator.cs
│   │   ├── NetAiFollower.cs
│   │   └── NetAiVisibilityGuard.cs
│   │
│   ├── NetTag/                    # 网络标签
│   │   ├── NetGrenadeTag.cs
│   │   ├── NetDropTag.cs
│   │   └── NetDestructibleTag.cs
│   │
│   └── Properties/
│       └── AssemblyInfo.cs
│
└── 鸭科夫联机Mod.sln              # 解决方案文件
```

**关键文件说明:**

- ⭐ `Directory.Build.props`: 集中管理所有路径配置,避免硬编码
- ⭐ `鸭科夫联机Mod.csproj`: 使用变量引用依赖,支持多环境
- ⭐ `Shared/`: 存放NuGet或手动下载的第三方库

---

## 🎓 进阶主题

### 调试技巧

1. **附加到游戏进程**
   - Visual Studio: 调试 → 附加到进程 → Duckov.exe
   - 设置断点进行调试

2. **查看IL代码**
   - 使用dnSpy打开编译后的DLL
   - 检查Harmony Patch是否正确生成

3. **网络包捕获**
   - 使用Wireshark监控LiteNetLib通信
   - 检查数据包格式和频率

### 性能优化

- 减少不必要的依赖引用
- 使用 `<Private>False</Private>` 避免复制Unity DLL
- Release模式编译启用优化

### 贡献代码

1. Fork本仓库
2. 创建功能分支
3. 提交Pull Request
4. 确保通过CI检查

---

## 📞 获取帮助 / Getting Help

- **Issues**: https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview/issues
- **Discussions**: https://github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview/discussions
- **Steam创意工坊**: https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282

---

## ✅ 配置检查清单 / Setup Checklist

- [ ] 安装了Visual Studio 2019/2022
- [ ] 安装了.NET Framework 4.8 SDK
- [ ] 游戏已正确安装
- [ ] 修改了 `Directory.Build.props` 中的游戏路径
- [ ] `Shared\` 目录包含 `0Harmony.dll` 和 `LiteNetLib.dll`
- [ ] 编译成功且无警告
- [ ] 生成的DLL大小正常(约100KB+)

完成以上所有步骤后,您就可以开始开发了! 🎉

---

**最后更新**: 2025-10-29  
**维护者**: Mr-sans-and-InitLoader-s-team

