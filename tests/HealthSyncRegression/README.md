Run from the repository root with the installed game available:

```powershell
$env:DUCKOV_GAME_DIRECTORY = 'D:\soft\steam\steamapps\common\Escape from Duckov'
dotnet run --project tests/HealthSyncRegression -c Release
```

The executable links the production HP policies, combat classification patch, health/buff/corpse serializers, buff replay ordering, spectator flow, death-settlement patch, corpse recovery bridge and death-loot spawn patch. Minimal engine stubs exercise ownership, interaction and save behavior without Unity. A deterministic player-loop and Continue-button substitute verifies that host and client death continuations wait through spectating and settlement, including repeated forced-end messages and disconnects. Camera checks verify that spectating keeps local input ownership, preserves the selected teammate across list changes and avoids restarting the same target blend. Corpse checks cover both owner roles, duplicate/old notifications, save-slot isolation, level reloads and death inventory reporting before native destruction. The native compatibility check reads the installed game's IL to verify the two preset loads targeted by the combat patch and native shelter protection. These checks do not run native combat, apply Harmony detours in Unity, render cameras/settlement screens or prove multiplayer performance.

Gameplay acceptance requires the same build on host and client:

- In the shelter, injure and heal each player. Compare each owner's HP with the other player's view while shelter regeneration runs. Repeat after changing maximum-health equipment and returning from a raid.
- In a raid, compare host and client shots using the same weapon, ammunition, range and target armor. Separate normal hits from critical hits. Verify AI death, loot, experience and killer-only quest credit.
- Repeat shooting the shelter target at 1 HP. Native non-raid protection must still prevent death.
- Join during combat and move out of and back into AI synchronization range. Older queued snapshots or movement packets must not restore earlier HP or revive dead AI.
- Die while another player survives, then end spectating through party extraction, all players dying or F8. Complete settlement and return to the shelter once. Repeat for a dead host and a dead client, and wait over ten seconds before clicking Continue.
- While spectating, move the mouse and press movement/aim keys. Verify smooth following without influencing the teammate; switch targets and let the selected teammate die or leave the map.
- Let A die and B partially loot A's current-raid corpse, extract and revisit the map. Repeat with host/client roles swapped. Only the exact remaining items and stack counts may return; opening alone must preserve them. Fully empty corpses must not return. Also check nested bag contents, attachments, durability and save/restart recovery. The native touched flag blocks the original full inventory while the mod restores the saved remainder.
- Trigger bleeding on each player, then treat it. Compare owner and mirror Buff IDs/layers before and after treatment. Repeat in direct/LAN mode; record whether frame rate recovers and whether the slowdown persists after treatment.
- Press F10 after each scenario. `[HealthSync]` records local and mirrored HP plus recent client hits settled by the host; `applied` is the actual HP difference, including shelter protection and remaining-health limits.
- For a persistent slowdown, capture F10 before bleeding, during the low frame rate and after treatment. The report also includes frame time, frame cap, vSync, focus, time scale, packet totals and Buff IDs/layers. A source/build check alone cannot establish the cause of the reported 160-to-20 FPS drop.
