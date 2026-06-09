# Codebase Audit

Generated: 2026-06-09

## Scope

This audit reviewed the local repository structure, build configuration, startup path, networking/RPC system, main gameplay synchronization services, Mod API layer, localization files, and available validation signals.

Runtime playtesting inside Escape From Duckov was not performed.

## Validation Performed

- Confirmed `DUCKOV_GAME_DIRECTORY` path:
  - `Z:\SteamLibrary\steamapps\common\Escape from Duckov`
- Confirmed game-managed DLLs are present, including:
  - `UnityEngine.dll`
  - `TeamSoda.Duckov.Core.dll`
- Restored NuGet packages with isolated temporary `APPDATA`, temporary `NuGet.Config`, and temporary package cache.
- Built Release configuration successfully:
  - `dotnet build EscapeFromDuckovCoopMod.sln -c Release --no-restore /p:DUCKOV_MODS_DIRECTORY=...`
- Parsed all `Localization/*.json` files successfully with `ConvertFrom-Json`.
- Checked for test projects and test files. None were found.

Build result:

- 0 errors
- 26 warnings

## Findings

### P1 - Failed LiteNetLib start is still treated as a running network

`NetService.StartNetwork()` stores the return value from `netManager.Start(...)`, logs failure, but continues and sets `networkStarted = true` regardless of whether the manager actually started.

Relevant files:

- `EscapeFromDuckovCoopMod/Main/NetService.cs:385`
- `EscapeFromDuckovCoopMod/Main/NetService.cs:397`
- `EscapeFromDuckovCoopMod/Main/NetService.cs:413`

Impact:

- If the host port is already in use or a client socket cannot start, UI and update loops can enter the "network started" path with a stopped `NetManager`.
- Later sync code can send, poll, or update against a network object that is not actually running.
- This makes connection failures harder to diagnose and can cause misleading status text.

Recommendation:

- Return early when `Start(...)` returns false.
- Only assign `networkStarted = true` after the start succeeds.
- Reset `netManager`, `writer`, `connectedPeer`, and status consistently on failure.

### P1 - Network packet deserialization is not exception-safe

Incoming packets are routed through `RpcRegistry.TryHandle()`, which invokes message deserialization and the handler. The receive path recycles the packet only after `TryHandle()` returns. If a malformed or truncated packet throws during `Deserialize()`, the exception bubbles out of the receive callback and the reader is not recycled on that path.

Relevant files:

- `EscapeFromDuckovCoopMod/Main/Loader/Mod.cs:528`
- `EscapeFromDuckovCoopMod/Main/Loader/Mod.cs:530`
- `EscapeFromDuckovCoopMod/Net/Rpc/RpcRegistry.cs:109`
- `EscapeFromDuckovCoopMod/Net/Rpc/RpcRegistry.cs:127`

Impact:

- A bad packet can interrupt network processing.
- In a hostile or unstable network environment, malformed packets can become a denial-of-service vector.
- Packet reader recycling depends on the happy path.

Recommendation:

- Wrap RPC dispatch in `try/finally` at the receive boundary so `reader.Recycle()` always runs exactly once.
- Log opcode, peer, channel, and exception.
- Consider adding `AvailableBytes` guards in high-risk message deserializers with large arrays and strings.

### P1 - Server trusts client loot and damage requests

The server accepts client requests for loot put/take and player damage with limited authoritative validation. It resolves the target inventory or target player, then applies the request. The reviewed paths do not prove the sender is near the lootbox, currently viewing it, owns the source item, or is allowed to damage the target in the claimed way.

Relevant files:

- `EscapeFromDuckovCoopMod/Main/SceneService/LootNet.cs:839`
- `EscapeFromDuckovCoopMod/Main/SceneService/LootNet.cs:956`
- `EscapeFromDuckovCoopMod/Main/Health/HealthM.cs:243`
- `EscapeFromDuckovCoopMod/Main/Weapon/WeaponHandle.cs:410`

Impact:

- Fine for trusted friend co-op.
- Risky for public or semi-public lobbies.
- A modified client can potentially inject item snapshots, remove loot remotely, or forward damage requests outside normal gameplay constraints.

Recommendation:

- Define the intended trust model explicitly.
- For public lobbies, add server-side checks:
  - sender is in same scene as loot/target
  - sender is within interaction range
  - sender is a registered viewer for the loot inventory
  - item source and count are valid
  - damage request is consistent with recent server-observed weapon/projectile/melee events

### P2 - Mod API replay request is asymmetric

`ModNetworkApi` attempts replay when its inbound queue drops messages. It calls `backend.SendReplayRequest(sender, channel)`. The active backend implements replay requests through `CoopTool.SendRpcTo(...)`, but `SendRpcTo` only sends when `service.IsServer` is true. That means client-side replay requests to the server appear to be dropped.

Relevant files:

- `EscapeFromDuckovModApi/ModNetworkApi.cs:286`
- `EscapeFromDuckovCoopMod/Main/ModApi/NetServiceModNetworkBackend.cs:39`
- `EscapeFromDuckovCoopMod/Main/CoopTool.cs:189`
- `EscapeFromDuckovCoopMod/Main/CoopTool.cs:193`

Impact:

- If a client drops queued Mod API messages from the server, it may not be able to request replay.
- Replay behavior may only work in the server-to-client direction when the server sends the request.

Recommendation:

- Split replay request into `SendReplayRequestToServer()` and `SendReplayRequestToPeer()` or make the backend choose `CoopTool.SendRpc(...)` on clients.
- Add a focused integration test or in-game diagnostic for client-side queue overflow and replay recovery.

### P2 - Mod API per-peer replay cache is not cleaned on disconnect

`ModNetworkApi` stores last-sent payloads in `_lastSentToPeer`, keyed by `NetPeer`. `NotifyPeerDisconnected()` raises an event but does not remove cached entries for the disconnected peer.

Relevant files:

- `EscapeFromDuckovModApi/ModNetworkApi.cs:43`
- `EscapeFromDuckovModApi/ModNetworkApi.cs:152`
- `EscapeFromDuckovModApi/ModNetworkApi.cs:315`
- `EscapeFromDuckovModApi/ModNetworkApi.cs:349`

Impact:

- Long-running hosts can accumulate stale per-peer payload caches as players join and leave.
- If `NetPeer` identity or hash reuse ever occurs, stale replay data could be selected incorrectly.

Recommendation:

- Remove `_lastSentToPeer[peer]` in `NotifyPeerDisconnected(peer)`.
- Consider limiting cached payload size per peer/channel.

### P2 - Startup and init failures are swallowed silently

`ModBehaviour.OnEnable()` patches and creates persistent objects every time it runs. `SafeInit<T>()` catches and ignores all exceptions, so a failed component init can leave a partially loaded mod with no diagnostic trail.

Relevant files:

- `EscapeFromDuckovCoopMod/Main/Loader/Loader.cs:23`
- `EscapeFromDuckovCoopMod/Main/Loader/Loader.cs:25`
- `EscapeFromDuckovCoopMod/Main/Loader/Loader.cs:28`
- `EscapeFromDuckovCoopMod/Main/Loader/Loader.cs:41`
- `EscapeFromDuckovCoopMod/Main/Loader/Loader.cs:84`
- `EscapeFromDuckovCoopMod/Main/Loader/Loader.cs:92`

Impact:

- If a single component init fails, later symptoms appear elsewhere.
- Re-enable behavior can duplicate persistent objects unless the host mod loader guarantees one enable per process.

Recommendation:

- Log exceptions in `SafeInit<T>()` with component type.
- Add an idempotency guard around global startup.
- Prefer explicit singleton destroy or reuse for persistent objects.

### P2 - Build project references `ECM2.dll` under the wrong assembly name

Both project files include two references named `UnityEngine.UIModule`; the second points to `ECM2.dll`. The build currently succeeds, but the metadata is misleading and can produce fragile assembly resolution behavior in IDEs and tooling.

Relevant files:

- `EscapeFromDuckovCoopMod/EscapeFromDuckovCoopMod.csproj:266`
- `EscapeFromDuckovCoopMod/EscapeFromDuckovCoopMod.csproj:269`
- `EscapeFromDuckovModApi/EscapeFromDuckovModApi.csproj:267`
- `EscapeFromDuckovModApi/EscapeFromDuckovModApi.csproj:270`

Recommendation:

- Change the second reference to `Include="ECM2"`.
- Consider centralizing game assembly references to reduce duplication between the two projects.

### P3 - Localization uses ad hoc JSON parsing

The localization JSON files are valid JSON, but `CoopLocalization` parses them with manual `IndexOf` and `Substring` logic. This will mishandle escaped quotes, escaped backslashes, nested braces in values, and other valid JSON edge cases.

Relevant files:

- `EscapeFromDuckovCoopMod/Main/Localization/LocalizationManager.cs:122`
- `EscapeFromDuckovCoopMod/Main/Localization/LocalizationManager.cs:147`
- `EscapeFromDuckovCoopMod/Main/Localization/LocalizationManager.cs:201`

Impact:

- Translation strings can silently truncate or fail when text contains escaped punctuation.
- Translators can create valid JSON that the mod parser cannot read.

Recommendation:

- Use `Newtonsoft.Json`, already available from the game-managed references, or a small DTO parsed by a robust JSON reader.
- Add a validation script that checks duplicate keys and format placeholders.

### P3 - Build warnings indicate stale or inconsistent code

Release build succeeded, but produced 26 warnings:

- Nullable annotations used while nullable context is disabled.
- Unreachable code in `COOPManager.StripAllHandItems()`.
- Several unused or never-assigned fields.

Representative files:

- `EscapeFromDuckovCoopMod/Main/COOPManager.cs:549`
- `EscapeFromDuckovCoopMod/Main/Diagnostics/PerformanceDiagnostics.cs:62`
- `EscapeFromDuckovCoopMod/Main/NetService.cs:105`
- `EscapeFromDuckovCoopMod/Main/UI/WaitingSynchronizationUI.cs:21`

Recommendation:

- Either enable nullable consistently or remove nullable annotations.
- Remove dead fields and unreachable code where confirmed obsolete.
- Keep warning count low enough that new warnings remain visible.

## Architecture Notes

### Strengths

- The project has a clear separation between transport, RPC messages, RPC handlers, and gameplay services.
- AI sync has queue limits, frame budgets, snapshot refreshes, and drop recovery concepts.
- Loot sync uses inventory versions and viewer sets.
- Scene sync has an explicit vote, ready, and gate model.
- Build version checks reject mismatched clients.
- Localization data is externalized and copied with the mod output.

### Main Risk Areas

- Cross-frame scene and AI state machines are complex and should be protected by diagnostics and scenario tests.
- Host-authoritative validation is partial and should be aligned with the intended trust model.
- Many error paths are silent, which slows debugging.
- RPC deserialization assumes well-formed packets.
- There are no automated tests in the repository.

## Suggested Next Steps

1. Fix the `StartNetwork()` failed-start state bug.
2. Make receive-side RPC dispatch exception-safe.
3. Decide and document the network trust model.
4. Add server-side loot and damage validation if public lobbies are in scope.
5. Fix the `ECM2` project reference names.
6. Replace manual localization parsing with a real JSON parser.
7. Add at least smoke tests or diagnostics for:
   - direct host start failure
   - client connect version mismatch
   - RPC malformed packet handling
   - loot open/take/put round trip
   - Mod API replay request from client
   - AI snapshot chunk queue overflow and recovery
