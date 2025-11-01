// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using Duckov.Utilities;

namespace EscapeFromDuckovCoopMod;

public class ClientPlayerApply
{
    private const float WeaponApplyDebounce = 0.20f; // 200ms 去抖窗口

    private readonly Dictionary<string, string> _lastWeaponAppliedByPlayer = new();
    private readonly Dictionary<string, float> _lastWeaponAppliedTimeByPlayer = new();
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;


    public async UniTask ApplyEquipmentUpdate_Client(string playerId, int slotHash, string itemId)
    {
        if (NetService.Instance.IsSelfId(playerId)) return;
        if (!clientRemoteCharacters.TryGetValue(playerId, out var remoteObj) || remoteObj == null) return;

        var characterModel = remoteObj.GetComponent<CharacterMainControl>().characterModel;
        if (characterModel == null) return;

        if (string.IsNullOrEmpty(itemId))
        {
            if (slotHash == 100) COOPManager.ChangeArmorModel(characterModel, null);
            if (slotHash == 200) COOPManager.ChangeHelmatModel(characterModel, null);
            if (slotHash == 300) COOPManager.ChangeFaceMaskModel(characterModel, null);
            if (slotHash == 400) COOPManager.ChangeBackpackModel(characterModel, null);
            if (slotHash == 500) COOPManager.ChangeHeadsetModel(characterModel, null);
            return;
        }

        string slotName = null;
        if (slotHash == CharacterEquipmentController.armorHash)
        {
            slotName = "armorSlot";
        }
        else if (slotHash == CharacterEquipmentController.helmatHash)
        {
            slotName = "helmatSlot";
        }
        else if (slotHash == CharacterEquipmentController.faceMaskHash)
        {
            slotName = "faceMaskSlot";
        }
        else if (slotHash == CharacterEquipmentController.backpackHash)
        {
            slotName = "backpackSlot";
        }
        else if (slotHash == CharacterEquipmentController.headsetHash)
        {
            slotName = "headsetSlot";
        }
        else
        {
            if (!string.IsNullOrEmpty(itemId) && int.TryParse(itemId, out var ids))
            {
                var item = await COOPManager.GetItemAsync(ids);
                if (item == null) Debug.LogWarning($"无法获取物品: ItemId={itemId}，槽位 {slotHash} 未更新");
                if (slotHash == 100) COOPManager.ChangeArmorModel(characterModel, item);
                if (slotHash == 200) COOPManager.ChangeHelmatModel(characterModel, item);
                if (slotHash == 300) COOPManager.ChangeFaceMaskModel(characterModel, item);
                if (slotHash == 400) COOPManager.ChangeBackpackModel(characterModel, item);
                if (slotHash == 500) COOPManager.ChangeHeadsetModel(characterModel, item);
            }

            return;
        }

        try
        {
            if (int.TryParse(itemId, out var ids))
            {
                var item = await COOPManager.GetItemAsync(ids);
                if (item != null)
                {
                    if (slotName == "armorSlot") COOPManager.ChangeArmorModel(characterModel, item);
                    if (slotName == "helmatSlot") COOPManager.ChangeHelmatModel(characterModel, item);
                    if (slotName == "faceMaskSlot") COOPManager.ChangeFaceMaskModel(characterModel, item);
                    if (slotName == "backpackSlot") COOPManager.ChangeBackpackModel(characterModel, item);
                    if (slotName == "headsetSlot") COOPManager.ChangeHeadsetModel(characterModel, item);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"更新装备失败(客户端): {playerId}, SlotHash={slotHash}, ItemId={itemId}, 错误: {ex.Message}");
        }
    }

    // 客户端：按 玩家ID 应用武器（幂等 + 去抖 + 杀残留 agent + 只清目标 + 等一帧再挂）
    public async UniTask ApplyWeaponUpdate_Client(string playerId, int slotHash, string itemId)
    {
        if (NetService.Instance.IsSelfId(playerId)) return;

        if (!clientRemoteCharacters.TryGetValue(playerId, out var remoteObj) || remoteObj == null) return;
        var cm = remoteObj.GetComponent<CharacterMainControl>();
        var model = cm ? cm.characterModel : null;
        if (model == null) return;

        var key = $"{playerId}:{slotHash}";
        var want = itemId ?? string.Empty;
        if (_lastWeaponAppliedByPlayer.TryGetValue(key, out var last) && last == want) return;
        if (_lastWeaponAppliedTimeByPlayer.TryGetValue(key, out var ts))
            if (Time.time - ts < WeaponApplyDebounce && last == want)
                return;
        _lastWeaponAppliedByPlayer[key] = want;
        _lastWeaponAppliedTimeByPlayer[key] = Time.time;

        var socket = CoopTool.ResolveSocketOrDefault(slotHash);

        try
        {
            if (!string.IsNullOrEmpty(itemId) && int.TryParse(itemId, out var typeId))
            {
                var item = await COOPManager.GetItemAsync(typeId);
                if (item != null)
                {
                    CoopTool.SafeKillItemAgent(item);

                    CoopTool.ClearWeaponSlot(model, socket);
                    await UniTask.NextFrame();

                    COOPManager.ChangeWeaponModel(model, item, socket);

                    var gunSetting = item.GetComponent<ItemSetting_Gun>();
                    var pfb = gunSetting && gunSetting.bulletPfb
                        ? gunSetting.bulletPfb
                        : GameplayDataSettings.Prefabs.DefaultBullet;
                    LoaclPlayerManager.Instance._projCacheByWeaponType[typeId] = pfb;
                    LoaclPlayerManager.Instance._muzzleFxCacheByWeaponType[typeId] = gunSetting ? gunSetting.muzzleFxPfb : null;
                }
            }
            else
            {
                CoopTool.ClearWeaponSlot(model, socket);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"更新武器失败(客户端): {playerId}, Slot={socket}, ItemId={itemId}, 错误: {ex.Message}");
        }
    }
}