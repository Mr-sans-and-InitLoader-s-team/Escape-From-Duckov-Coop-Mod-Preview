# Shared Libraries / 共享库目录

此目录用于存放项目所需的第三方DLL文件。

## 所需文件 / Required Files

请将以下DLL文件复制到此目录:

### 1. 0Harmony.dll
- **来源**: NuGet包或从游戏目录复制
- **版本**: 2.3.3+ 
- **NuGet**: `Install-Package Lib.Harmony -Version 2.4.1`
- **或手动下载**: https://github.com/pardeike/Harmony/releases

### 2. LiteNetLib.dll
- **来源**: NuGet包
- **版本**: 1.1.0
- **NuGet**: `Install-Package LiteNetLib -Version 1.1.0`
- **或手动下载**: https://github.com/RevenantX/LiteNetLib/releases

## 如何获取这些文件 / How to Get These Files

### 方法1: 使用NuGet (推荐)

在项目根目录执行:

```powershell
# 安装Harmony
nuget install Lib.Harmony -Version 2.4.1 -OutputDirectory packages

# 安装LiteNetLib  
nuget install LiteNetLib -Version 1.1.0 -OutputDirectory packages

# 复制DLL到Shared目录
Copy-Item "packages\Lib.Harmony.2.4.1\lib\net48\0Harmony.dll" "Shared\"
Copy-Item "packages\LiteNetLib.1.1.0\lib\netstandard2.1\LiteNetLib.dll" "Shared\"
```

### 方法2: 从游戏目录复制

某些DLL可能已经存在于游戏的Managed目录中:

```
E:\SteamLibrary\steamapps\common\Escape from Duckov\Duckov_Data\Managed\
```

### 方法3: 手动下载

从GitHub Releases页面下载对应版本的DLL文件并复制到此目录。

## 注意事项 / Notes

- ⚠️ **请勿将DLL文件提交到Git仓库**（已在.gitignore中配置）
- ⚠️ 每个开发者需要自行获取这些文件
- ⚠️ 确保DLL版本与项目要求一致
- ✅ 编译前检查此目录是否包含所需文件

