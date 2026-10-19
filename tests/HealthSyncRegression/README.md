Run from the repository root with the installed game available:

```powershell
$env:DUCKOV_GAME_DIRECTORY = 'D:\soft\steam\steamapps\common\Escape from Duckov'
dotnet run --project tests/HealthSyncRegression -c Release
```

The executable links the production snapshot-ordering, replica HP policy, combat classification patch and health broadcast serializer. Minimal engine stubs exercise ownership and event behavior without Unity. The native compatibility check reads the installed game's IL to verify the two preset loads targeted by the combat patch and the presence of native shelter protection. It does not run native combat, apply Harmony detours in Unity, or prove multiplayer behavior.

Gameplay acceptance requires the same build on host and client:

- In the shelter, injure and heal each player. Compare each owner's HP with the other player's view while shelter regeneration runs. Repeat after changing maximum-health equipment and returning from a raid.
- In a raid, compare host and client shots using the same weapon, ammunition, range and target armor. Separate normal hits from critical hits. Verify AI death, loot and killer-only quest credit.
- Repeat shooting the shelter target at 1 HP. Native non-raid protection must still prevent death.
- Join during combat and move out of and back into AI synchronization range. Older queued snapshots or movement packets must not restore earlier HP or revive dead AI.
- Press F10 after each scenario. `[HealthSync]` records local and mirrored HP plus recent client hits settled by the host; `applied` is the actual HP difference, including shelter protection and remaining-health limits.
