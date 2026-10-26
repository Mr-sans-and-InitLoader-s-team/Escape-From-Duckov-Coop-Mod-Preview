using Duckov;
using Duckov.BlackMarkets;
using Duckov.Economy;
using Duckov.PerkTrees;
using Duckvo.Beacons;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeProgress
{
    internal static Teams KillCreditTeam(CharacterMainControl character)
    {
        if (SharedHome.Active && NetService.Instance.IsServer &&
            NetService.Instance.remoteCharacters.Values.Any(go => go && go.GetComponent<CharacterMainControl>() == character)) return Teams.player;
        return character.Team;
    }
    internal static void Init()
    {
        EXPManager.onExpChanged += _ => SharedHome.MarkDirty();
        EconomyManager.OnMoneyChanged += (_, _) => SharedHome.MarkDirty();
        EconomyManager.OnItemUnlockStateChanged += _ => SharedHome.MarkDirty();
        Perk.OnPerkUnlockConfirmed += _ => SharedHome.MarkDirty();
        CraftingManager.OnFormulaUnlocked += _ => SharedHome.MarkDirty();
        BeaconManager.OnBeaconUnlocked += (_, _) => SharedHome.MarkDirty();
        BlackMarket.onRefreshChanceChanged += _ => SharedHome.MarkDirty();
        PlayerStorage.OnPlayerStorageChange += (_, _, _) => SharedHome.MarkDirty();
        PlayerStorage.OnTakeBufferItem += SharedHome.MarkDirty;
        PlayerStorage.OnItemAddedToBuffer += _ => SharedHome.MarkDirty();
        CraftingManager.OnItemCrafted += (_, _) => SharedHome.MarkDirty();
        LevelManager.OnLevelInitialized += SharedHome.MarkDirty;
        Health.OnDead += OnDead;
    }
    private static void OnDead(Health victim, DamageInfo damage)
    {
        if (!SharedHome.Active || !NetService.Instance.IsServer || !victim || victim.team == Teams.player) return;
        var attacker = damage.fromCharacter;
        if (!attacker || attacker == CharacterMainControl.Main) return;
        if (!NetService.Instance.remoteCharacters.Values.Any(go => go && go.GetComponent<CharacterMainControl>() == attacker)) return;
        var item = victim.TryGetCharacter()?.CharacterItem;
        if (item) EXPManager.AddExp(item.GetInt("Exp"));
        SharedHome.MarkDirty();
    }
}
