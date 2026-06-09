# Code Structure

Generated: 2026-06-09

## Repository Overview

This repository contains a C# Unity mod for Escape From Duckov cooperative play. It is organized as a Visual Studio solution with two projects:

- `EscapeFromDuckovCoopMod`: the main runtime mod, Harmony patches, networking, UI, synchronization services, and Steam P2P integration.
- `EscapeFromDuckovModApi`: a small public API layer for other mods to send and receive coop network messages.

The solution targets `netstandard2.1` and depends on DLLs from the local game installation under `Duckov_Data/Managed`, plus checked-in shared dependencies under `Shared/`.

## Top-Level Layout

```text
.
|-- EscapeFromDuckovCoopMod.sln
|-- Directory.Build.props
|-- EscapeFromDuckovCoopMod/
|   |-- Main/
|   |-- Net/
|   |-- NetTag/
|   |-- Patch/
|   |-- SyncData/
|   |-- Utils/
|   |-- Properties/
|   |-- EscapeFromDuckovCoopMod.csproj
|-- EscapeFromDuckovModApi/
|   |-- ModNetworkApi.cs
|   |-- ModNetworkPump.cs
|   |-- IModNetworkBackend.cs
|   |-- EscapeFromDuckovModApi.csproj
|-- Localization/
|-- Shared/
|-- README.md
|-- README_EN.md
```

## Build And Runtime Dependencies

- `Directory.Build.props` derives these paths from `DUCKOV_GAME_DIRECTORY`:
  - `DUCKOV_DATA_DIRECTORY`
  - `DUCKOV_GAME_MANAGED`
  - `DUCKOV_MODS_DIRECTORY`
- Both projects reference many game-managed assemblies directly from `$(DUCKOV_GAME_MANAGED)`.
- `Shared/` contains:
  - `0Harmony.dll`
  - `LiteNetLib.dll`
- Both projects reference `Polyfill` version `8.9.0`.
- The main project copies its DLL and localization files to `$(DUCKOV_MODS_DIRECTORY)\$(ProjectName)` after build.

For verification without writing to the game directory, override `DUCKOV_MODS_DIRECTORY`:

```powershell
$env:DUCKOV_GAME_DIRECTORY = 'Z:\SteamLibrary\steamapps\common\Escape from Duckov'
dotnet build EscapeFromDuckovCoopMod.sln -c Release --no-restore /p:DUCKOV_MODS_DIRECTORY=F:\Escape-From-Duckov-Coop-Mod-Preview\.build\Mods
```

## Runtime Boot Sequence

Primary entry point:

- `EscapeFromDuckovCoopMod/Main/Loader/Loader.cs`

Flow:

1. `ModBehaviour.OnEnable()` creates a Harmony instance and patches all discovered Harmony patches.
2. It creates a persistent `COOP_MOD_1` object and attaches `NetService`.
3. `COOPManager.InitManager()` initializes the main non-MonoBehaviour service singletons.
4. `Loader()` initializes localization, creates a persistent `COOP_MOD_` object, and attaches runtime MonoBehaviours.
5. `DeferredInit()` calls `Init()` on registered runtime components.

Important runtime components attached at startup:

- `SteamP2PLoader`
- `Send_ClientStatus`
- `HealthM`
- `LocalPlayerManager`
- `SendLocalPlayerStatus`
- `SendLocalVehicleStatus`
- `Spectator`
- `DeadLootBox`
- `LootManager`
- `SceneNet`
- `MModUI`
- `CoopAISettings`
- `AISyncSettingsUI`
- `CoopLootSettings`
- `WaitingSynchronizationUI`
- `VersionOverlayTMP`

## Main Service Registry

`EscapeFromDuckovCoopMod/Main/COOPManager.cs` is a static registry for most cross-cutting coop services:

- `HostPlayerApply`
- `ClientPlayerApply`
- `LootNet`
- `Door`
- `Destructible`
- `ExplosiveOilBarrel`
- `ExitSyncService`
- `FriendlyFireSync`
- `GrenadeM`
- `HurtM`
- `WeaponHandle`
- `Weather`
- `ClientHandle`
- `PublicHandleUpdate`
- `ItemNet`
- `Buff_`
- `WeaponRequest`
- `AISyncService`

This class also contains shared helper methods for item and equipment model handling.

## Networking

Core network service:

- `EscapeFromDuckovCoopMod/Main/NetService.cs`

Responsibilities:

- Starts and stops LiteNetLib networking.
- Maintains server/client mode.
- Tracks connected peers.
- Stores local and remote player status dictionaries.
- Handles LiteNetLib callbacks.
- Performs connection version checks using `BuildInfo.ModVersion`.
- Bridges `ModNetworkApi` through `NetServiceModNetworkBackend`.

Transport modes:

- `Direct`: LiteNetLib UDP direct networking.
- `SteamP2P`: Steam networking path with socket and LiteNetLib patches.

Steam P2P modules:

- `Net/Steam/SteamP2PLoader.cs`
- `Net/Steam/SteamP2PManager.cs`
- `Net/Steam/SteamLobbyManager.cs`
- `Net/Steam/SteamLobbyHelper.cs`
- `Net/Steam/SteamEndPointMapper.cs`
- `Patch/SteamP2P/Patch_Socket.cs`
- `Patch/SteamP2P/Patch_LiteNetLib.cs`
- `Patch/SteamP2P/PacketSignature.cs`

## RPC System

Core files:

- `Net/Rpc/RpcAttribute.cs`
- `Net/Rpc/RpcDescriptor.cs`
- `Net/Rpc/RpcRegistry.cs`
- `Net/Rpc/RpcContext.cs`
- `Net/Rpc/IRpcMessage.cs`
- `Main/CoopTool.cs`

Pattern:

1. Each RPC message is a struct implementing `IRpcMessage`.
2. Messages carry `[Rpc(Op.X, DeliveryMethod.Y, RpcDirection.Z)]`.
3. `RpcRegistry.Initialize()` registers message types to handlers.
4. `CoopTool.SendRpc()` serializes opcode plus payload and sends according to direction.
5. `ModBehaviourF.OnNetworkReceive()` reads the opcode and gives the packet to `RpcRegistry.TryHandle()`.

Handler folders:

- `Net/Rpc/Handler/RPCPlayer.cs`
- `Net/Rpc/Handler/RPCHealth.cs`
- `Net/Rpc/Handler/RPCWeapon.cs`
- `Net/Rpc/Handler/RPCLoot.cs`
- `Net/Rpc/Handler/RPCScene.cs`
- `Net/Rpc/Handler/RPCEnvironment.cs`
- `Net/Rpc/Handler/RPCAI.cs`
- `Net/Rpc/Handler/RPCItem.cs`
- `Net/Rpc/Handler/RPCAudio.cs`
- `Net/Rpc/Handler/RPCModApi.cs`
- `Net/Rpc/Handler/RPCDiagnostics.cs`

Message groups:

- `Net/Rpc/Messages/Player`
- `Net/Rpc/Messages/Health`
- `Net/Rpc/Messages/Weapon`
- `Net/Rpc/Messages/Loot`
- `Net/Rpc/Messages/Scene`
- `Net/Rpc/Messages/Environment`
- `Net/Rpc/Messages/AI`
- `Net/Rpc/Messages/Item`
- `Net/Rpc/Messages/Audio`
- `Net/Rpc/Messages/Vehicle`
- `Net/Rpc/Messages/ModApi`
- `Net/Rpc/Messages/Diagnostics`

## Gameplay Synchronization Areas

### Player Sync

Key files:

- `Main/LocalPlayer/LocalPlayerManager.cs`
- `Main/LocalPlayer/SendLocalPlayerStatus.cs`
- `Main/LocalPlayer/SendLocalVehicleStatus.cs`
- `Main/ClientService/ClientPlayerApply.cs`
- `Main/HostService/HostPlayerApply.cs`
- `Main/SceneService/CreateRemoteCharacter.cs`
- `Main/Player/FriendlyFireSync.cs`

Responsibilities:

- Track local player state.
- Send position, animation, loadout, vehicle status, and scene status.
- Create and update remote player character proxies.

### Scene And Raid Flow

Key files:

- `Main/SceneService/SceneNet.cs`
- `Main/SceneService/SceneM.cs`
- `Patch/Scene/*.cs`
- `Utils/SceneTriggerResetter.cs`

Responsibilities:

- Scene vote and ready state.
- Host-driven scene loading.
- Client scene gates.
- Scene-ready broadcasts.
- Scene transition cleanup.

### AI Sync

Key files:

- `Main/SceneService/AISyncService.cs`
- `Main/SceneService/AISyncTracker.cs`
- `Main/CoopAISettings.cs`
- `Main/AISyncSettingsPersistence.cs`
- `Net/Rpc/Messages/AI/*.cs`
- `Patch/Scene/AIPatch.cs`
- `Patch/Character/AIAwarenessPatch.cs`

Responsibilities:

- Server-side AI registration and ownership.
- Client-side AI replicas.
- Snapshot and state update queues.
- Activation and deactivation by distance.
- Health, buff, awareness, sound, pop text, and vehicle-like AI sync.

### Loot And Item Sync

Key files:

- `Main/SceneService/LootManager.cs`
- `Main/SceneService/LootNet.cs`
- `Main/SceneService/DeadLootBox.cs`
- `Main/Item/ItemNet.cs`
- `Main/Item/ItemTool.cs`
- `Net/Rpc/Messages/Loot/*.cs`
- `Net/Rpc/Messages/Item/*.cs`
- `Patch/Loot/*.cs`
- `Patch/Item/*.cs`

Responsibilities:

- Lootbox registry and identification.
- Lootbox snapshot and delta sync.
- Item stack, plug, unplug, put, take, and dead-loot flow.
- Dropped item spawn, pickup, despawn, and snapshot sync.

### Combat, Health, Buffs, And Weapons

Key files:

- `Main/Health/HealthM.cs`
- `Main/Health/HealthTool.cs`
- `Main/Health/HurtM.cs`
- `Main/Health/Buff.cs`
- `Main/Weapon/WeaponHandle.cs`
- `Main/Weapon/WeaponRequest.cs`
- `Main/Weapon/WeaponTool.cs`
- `Main/Weapon/GrenadeM.cs`
- `Patch/Character/HealthPatch.cs`
- `Patch/Item/GunPatch.cs`
- `Patch/Projectile/FakeProjectilePatch.cs`

Responsibilities:

- Health report and broadcast.
- Damage forwarding.
- Buff report and broadcast.
- Projectile and melee replication.
- Grenade spawn and explosion replication.
- Fake projectile registry and visual effects.

### Environment Sync

Key files:

- `Main/SceneService/Door.cs`
- `Main/SceneService/Destructible.cs`
- `Main/SceneService/ExplosiveOilBarrel.cs`
- `Main/SceneService/ExitSyncService.cs`
- `Main/SceneService/LevelDataBoolNet.cs`
- `Main/WeatherAndTime/Weather.cs`
- `NetTag/*.cs`
- `Patch/Scene/DoorPatch.cs`
- `Patch/Loot/*.cs`

Responsibilities:

- Door state.
- Destructible state and health.
- Explosive barrel active/dead state.
- Exit spawn synchronization.
- Clock and weather snapshots.
- Loot visibility snapshots.

## UI

Key files:

- `Main/UI/MModUI.cs`
- `Main/UI/MModUILayoutBuilder.cs`
- `Main/UI/MModUIComponents.cs`
- `Main/UI/AISyncSettingsUI.cs`
- `Main/UI/WaitingSynchronizationUI.cs`
- `Main/UI/VersionOverlayTMP.cs`
- `Main/UI/DamageStatsUI.cs`
- `Main/UI/ModUI.cs`

Responsibilities:

- Host/client controls.
- LAN and Steam lobby UI.
- Player list and status.
- Chat.
- Scene vote UI.
- AI sync settings.
- Waiting and synchronization overlay.
- Diagnostics overlays.

## Localization

Runtime loader:

- `Main/Localization/LocalizationManager.cs`

Data:

- `Localization/en-US.json`
- `Localization/zh-CN.json`
- `Localization/ja-JP.json`
- `Localization/ko-KR.json`
- `Localization/ru-RU.json`
- `Localization/de-DE.json`
- `Localization/pt-BR.json`

The main project links `Localization/*.json` into the output and copies them to the mod folder.

## Mod API Project

`EscapeFromDuckovModApi` exposes a channel-based API for other mods:

- `ModNetworkApi.RegisterHandler(channel, handler)`
- `ModNetworkApi.SendToServer(channel, payloadBuilder)`
- `ModNetworkApi.SendToClient(peer, channel, payloadBuilder)`
- `ModNetworkApi.Broadcast(channel, payloadBuilder)`

Messages are queued and dispatched through `ModNetworkPump`, with bounded backlog and a best-effort replay request mechanism.

## Data And State

Key files:

- `SyncData/CoopSyncDatabase.cs`
- `SyncData/SyncDataManger.cs`
- `SyncData/Coopbase.cs`
- `EscapeFromDuckovModApi/ItemSnapshots.cs`
- `EscapeFromDuckovModApi/AISyncModels.cs`

`CoopSyncDatabase` is the central in-memory registry for environment objects, AI, drops, and loot.

## Harmony Patches

Patch folders:

- `Patch/Character`
- `Patch/Input`
- `Patch/Item`
- `Patch/Loot`
- `Patch/Projectile`
- `Patch/Scene`
- `Patch/SteamP2P`
- `Patch/UI`

Patches intercept game runtime behavior and redirect selected local-only flows into host-authoritative or synchronized flows.

## Validation Snapshot

Validation run on 2026-06-09:

- Game path used: `Z:\SteamLibrary\steamapps\common\Escape from Duckov`
- `dotnet restore` succeeded with isolated temporary NuGet config and temporary package cache.
- `dotnet build EscapeFromDuckovCoopMod.sln -c Release --no-restore` succeeded.
- Build produced 26 warnings and 0 errors.
- Localization JSON files all parsed successfully with PowerShell `ConvertFrom-Json`.
- No test projects or test files were found.
