# 🦆 Escape From Duckov Coop Mod Preview

[![License](https://img.shields.io/badge/License-Modified%20AGPL--3.0-blue.svg)](LICENSE.txt)
[![Steam Workshop](https://img.shields.io/badge/Steam-Workshop-blue.svg)](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282)

English | **[简体中文](README.md)**

---

## 📖 Introduction

**Escape From Duckov Coop Mod Preview** is a multiplayer co-op mod developed for the game Escape From Duckov.

This mod enables stable LAN and online co-op gameplay in a game originally designed for single-player, featuring:

- 🎮 Multiplayer synchronization
- 🤖 AI behavior synchronization
- 📦 Loot sharing
- 👻 Death spectator mode
- ⚔️ Complete combat synchronization
- 🌐 LAN/Online multiplayer support

---

## 🎯 How to Use

### For Players

**No manual installation or build is required.**

Simply subscribe on Steam Workshop:

👉 **[Steam Workshop Page](https://steamcommunity.com/sharedfiles/filedetails/?id=3591341282)**

After subscribing, launch the game and enable the mod to start playing cooperatively.

### For Developers

If you want to build from source or contribute to development, please refer to the [Build Guide](#-build-guide).

---

## 🛠️ Build Guide

### Prerequisites

- Visual Studio 2019 or later
- .NET Framework 4.8
- Escape From Duckov game installed

### Step 1: Configure Environment Variables

Before building for the first time, you need to set up the game path environment variable.

#### Method 1: Use Automatic Configuration Script (Recommended)

1. Locate the `SetEnvVars_Permanent.bat` file in the project root directory
2. Double-click to run the script
3. Enter your game folder path when prompted

   **Example path**:
   ```
   C:\Steam\steamapps\common\Escape from Duckov
   ```

4. The script will automatically set the `DUCKOV_GAME_DIRECTORY` environment variable
5. **Important**: Close Visual Studio completely and reopen it to load the new environment variable

#### Method 2: Manual Configuration

1. Right-click "This PC" → "Properties" → "Advanced system settings" → "Environment Variables"
2. Under "User variables", click "New"
3. Variable name: `DUCKOV_GAME_DIRECTORY`
4. Variable value: Full path to your game's Managed folder
5. Click "OK" to save

### Step 2: Prepare Dependencies

Ensure the following DLL files are in the `Shared` folder:
- `0Harmony.dll`
- `LiteNetLib.dll`

### Step 3: Build the Project

1. Open `EscapeFromDuckovCoopMod.sln` solution
2. Select `Release` configuration
3. Right-click the solution → "Build Solution"

After successful compilation, output files will be in the `EscapeFromDuckovCoopMod/bin/Release/` directory.

### Troubleshooting

**Q: Build fails with missing DLL references?**

A: Make sure you have correctly set the `DUCKOV_GAME_DIRECTORY` environment variable and restarted Visual Studio.

**Q: Environment variable not working after setting?**

A: 
1. Verify the variable is set by running `echo %DUCKOV_GAME_DIRECTORY%` in command prompt
2. Ensure Visual Studio is completely closed (including background processes) before reopening

**Q: What if my path contains spaces or special characters?**

A: The script supports paths with spaces and parentheses, such as `Program Files (x86)`. Simply enter the complete path as-is.

---

## 🎯 Features

### Core Features
- ✅ Player position, actions, and equipment synchronization
- ✅ AI enemy state synchronization
- ✅ Loot box synchronization
- ✅ Door and destructible object synchronization
- ✅ Throwable items (grenades, etc.) synchronization
- ✅ Damage calculation and synchronization
- ✅ Death spectator mode

### Network Features
- 🌐 LAN multiplayer support
- 🌐 Internet multiplayer support
- ⚡ Optimized network performance
- 🔄 Automatic reconnection

---

## 💡 Credits

Special thanks to the following contributors:

- **Neko17** - Core development
- **Prototype-alpha** - Feature development and optimization
- **All debug and testing participants**

Thanks to the following open source projects:

- [HarmonyLib](https://github.com/pardeike/Harmony) - Runtime code patching framework
- [LiteNetLib](https://github.com/RevenantX/LiteNetLib) - UDP networking library

---

## 📄 License

This project is released under a **modified AGPL-3.0 License**.

All derived works must comply with the following terms:

- ❌ **No commercial use allowed**
- ❌ **No closed-source server deployment**
- ✅ **Attribution required**

For details, see:
- [LICENSE.txt](LICENSE.txt) - Full license text
- [LICENSE_RESTRICTIONS.txt](LICENSE_RESTRICTIONS.txt) - Additional restrictions

---

## 📞 Contact & Feedback

Feel free to report bugs or share suggestions through [Issues](../../issues) or [Discussions](../../discussions).

This project is still in preview — community contributions are highly appreciated!

---

## 🗺️ Roadmap

- [ ] More game mechanics synchronization
- [ ] Performance optimization
- [ ] Better error handling
- [ ] Comprehensive documentation

---

**⭐ If this project helps you, please give us a Star!**

