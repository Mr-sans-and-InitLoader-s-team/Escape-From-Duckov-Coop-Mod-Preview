# MapTeleport Mod 构建说明

## 环境变量配置

构建脚本使用环境变量 `DUCKOV_DLL_DIR` 来指定游戏 DLL 目录。

默认路径：`C:\SteamLibrary\steamapps\common\Escape from Duckov\Duckov_Data\Managed`

## 构建方法

### 方法 1: 使用批处理脚本 (推荐)

```cmd
build.bat
```

### 方法 2: 使用 PowerShell 脚本

```powershell
.\build.ps1
```

### 方法 3: 手动构建

```cmd
set DUCKOV_DLL_DIR=C:\SteamLibrary\steamapps\common\Escape from Duckov\Duckov_Data\Managed
dotnet build MapTeleport.csproj -c Release
```

## 自定义 DLL 路径

如果你的游戏安装在其他位置，可以：

### 临时设置（当前会话）

**CMD:**
```cmd
set DUCKOV_DLL_DIR=你的游戏路径\Duckov_Data\Managed
build.bat
```

**PowerShell:**
```powershell
$env:DUCKOV_DLL_DIR = "你的游戏路径\Duckov_Data\Managed"
.\build.ps1
```

### 永久设置（系统环境变量）

1. 右键"此电脑" → "属性"
2. 点击"高级系统设置"
3. 点击"环境变量"
4. 在"用户变量"中新建：
   - 变量名：`DUCKOV_DLL_DIR`
   - 变量值：`你的游戏路径\Duckov_Data\Managed`

### 修改构建脚本

直接编辑 `build.bat` 或 `build.ps1`，修改第一行的路径。

## 输出文件

构建成功后，DLL 文件位于：`bin\Release\MapTeleport.dll`

## 依赖的游戏 DLL

- Assembly-CSharp.dll
- UnityEngine.dll
- UnityEngine.CoreModule.dll
- UnityEngine.PhysicsModule.dll
- UniTask.dll
