# Mod安装指南 / Installation Guide

## 重要提示 / Important Notice

本Mod需要将所有依赖DLL一起部署，否则会出现加载错误。

This mod requires all dependency DLLs to be deployed together, otherwise loading errors will occur.

## 安装步骤 / Installation Steps

### 1. 获取Mod文件 / Get Mod Files

从Release页面下载最新版本，或者自己编译项目。

Download the latest version from the Releases page, or compile the project yourself.

### 2. 找到游戏Mod目录 / Locate Game Mod Directory

游戏Mod目录路径通常为：

Game mod directory is usually at:

```
<游戏安装目录>/Duckov_Data/Mods/
```

例如 / For example:
```
E:/SteamLibrary/steamapps/common/Escape from Duckov/Duckov_Data/Mods/
```

### 3. 创建Mod文件夹 / Create Mod Folder

在Mods目录下创建一个新文件夹，例如 `CoopMod`：

Create a new folder in the Mods directory, for example `CoopMod`:

```
Duckov_Data/Mods/CoopMod/
```

### 4. 复制所有必需文件 / Copy All Required Files

将以下文件复制到刚创建的文件夹中：

Copy the following files to the newly created folder:

```
从 鸭科夫联机Mod/bin/Release/ 或 bin/x64/Release/ 复制：
Copy from 鸭科夫联机Mod/bin/Release/ or bin/x64/Release/:

✓ 鸭科夫联机Mod.dll          (主Mod文件 / Main mod file)
✓ LiteNetLib.dll              (网络库 / Networking library) ⚠️ 必需 / REQUIRED
✓ 0Harmony.dll                (Hook库 / Hooking library) ⚠️ 必需 / REQUIRED
```

最终文件结构应该是：

Final file structure should be:

```
Duckov_Data/Mods/CoopMod/
├── 鸭科夫联机Mod.dll
├── LiteNetLib.dll
└── 0Harmony.dll
```

### 5. 启动游戏 / Launch Game

启动游戏，在游戏内的Mod管理器中启用该Mod。

Launch the game and enable the mod in the in-game mod manager.

## 使用说明 / Usage Notes

### 密码保护房间 / Password Protected Rooms

当创建带密码的Steam房间时，房间类型会自动变为"好友可见"，以确保密码保护功能正常工作。这是Steam API的限制。

When creating a Steam room with a password, the room type will automatically become "Friends Only" to ensure password protection works correctly. This is a limitation of the Steam API.

## 常见问题 / Troubleshooting

### TypeLoadException: Could not load file or assembly 'LiteNetLib'

**原因 / Cause:** 缺少LiteNetLib.dll文件

**解决方案 / Solution:** 
1. 检查Mod文件夹中是否有 `LiteNetLib.dll`
2. 确保LiteNetLib.dll版本为1.1.0
3. 重新从编译输出目录复制所有DLL文件

**Cause:** Missing LiteNetLib.dll file

**Solution:**
1. Check if `LiteNetLib.dll` exists in the mod folder
2. Ensure LiteNetLib.dll version is 1.1.0
3. Re-copy all DLL files from the build output directory

### Steam房间创建失败: k_EResultAccessDenied

**原因 / Cause:** 
1. 尝试创建带密码的公开房间（Steam不支持）
2. Steam权限配置问题

**解决方案 / Solution:**
1. 如果需要密码保护，房间会自动设为"好友可见"
2. 确保游戏通过Steam启动
3. 检查Steam账号状态正常

**Cause:**
1. Trying to create a public room with password (not supported by Steam)
2. Steam permission issues

**Solution:**
1. Rooms with passwords are automatically set to "Friends Only"
2. Ensure the game is launched through Steam
3. Check Steam account status is normal

### TypeLoadException: Could not load file or assembly '0Harmony'

**原因 / Cause:** 缺少0Harmony.dll文件

**解决方案 / Solution:** 同上，确保复制了所有依赖文件

**Cause:** Missing 0Harmony.dll file

**Solution:** Same as above, ensure all dependency files are copied

### Mod无法在游戏中显示 / Mod doesn't show in game

**可能原因 / Possible causes:**
1. 文件夹位置不正确
2. DLL文件损坏
3. 游戏版本不兼容

**解决方案 / Solutions:**
1. 确认Mod文件夹在正确的Mods目录下
2. 重新下载或编译Mod
3. 检查游戏版本是否受支持

## 卸载 / Uninstallation

直接删除Mod文件夹即可。

Simply delete the mod folder.

## 更新 / Update

下载新版本后，替换所有DLL文件。建议先删除旧文件夹，再创建新文件夹。

After downloading a new version, replace all DLL files. It's recommended to delete the old folder first and then create a new one.

---

如有其他问题，请在GitHub Issues中报告。

For other issues, please report in GitHub Issues.

