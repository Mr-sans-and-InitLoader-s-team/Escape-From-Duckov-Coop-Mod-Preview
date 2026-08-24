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
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

public class HostPlayerApply
{
    private const float WeaponApplyDebounce = 0.20f; // 200ms 去抖窗口

    private readonly Dictionary<string, string> _lastEquipmentAppliedByRemote = new();
    private readonly Dictionary<string, string> _lastWeaponAppliedByPeer = new();
    private readonly Dictionary<string, float> _lastWeaponAppliedTimeByPeer = new();
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


    public async UniTask ApplyEquipmentUpdate(NetPeer peer, int slotHash, string itemId)
    {
        if (!remoteCharacters.TryGetValue(peer, out var remoteObj) || remoteObj == null) return;

        var characterModel = remoteObj.GetComponent<CharacterMainControl>().characterModel;
        if (characterModel == null) return;

        var slotName = ResolveEquipmentSlotName(slotHash);
        if (slotName == null) return;

        var want = itemId ?? string.Empty;
        var key = $"{remoteObj.GetInstanceID()}:{slotName}";
        if (_lastEquipmentAppliedByRemote.TryGetValue(key, out var last) && last == want)
            return;

        try
        {
            if (string.IsNullOrEmpty(want))
            {
                ApplyEquipmentVisual(characterModel, slotName, null);
                _lastEquipmentAppliedByRemote[key] = want;
                return;
            }

            if (!int.TryParse(want, out var typeId))
                return;

            var item = await COOPManager.GetItemAsync(typeId);
            if (item == null)
            {
                Debug.LogWarning($"无法获取物品: ItemId={want}，槽位 {slotHash} 未更新");
                return;
            }

            ApplyEquipmentVisual(characterModel, slotName, item);
            _lastEquipmentAppliedByRemote[key] = want;
        }
        catch (Exception ex)
        {
            Debug.LogError($"更新装备失败(主机): {peer?.EndPoint}, SlotHash={slotHash}, ItemId={itemId}, 错误: {ex.Message}");
        }
    }

    private static string ResolveEquipmentSlotName(int slotHash)
    {
        if (slotHash == 100 || slotHash == CharacterEquipmentController.armorHash) return "armorSlot";
        if (slotHash == 200 || slotHash == CharacterEquipmentController.helmatHash) return "helmatSlot";
        if (slotHash == 300 || slotHash == CharacterEquipmentController.faceMaskHash) return "faceMaskSlot";
        if (slotHash == 400 || slotHash == CharacterEquipmentController.backpackHash) return "backpackSlot";
        if (slotHash == 500 || slotHash == CharacterEquipmentController.headsetHash) return "headsetSlot";
        return null;
    }

    private static void ApplyEquipmentVisual(CharacterModel characterModel, string slotName, Item item)
    {
        if (slotName == "armorSlot") COOPManager.ChangeArmorModel(characterModel, item);
        else if (slotName == "helmatSlot") COOPManager.ChangeHelmatModel(characterModel, item);
        else if (slotName == "faceMaskSlot") COOPManager.ChangeFaceMaskModel(characterModel, item);
        else if (slotName == "backpackSlot") COOPManager.ChangeBackpackModel(characterModel, item);
        else if (slotName == "headsetSlot") COOPManager.ChangeHeadsetModel(characterModel, item);
    }

    public async UniTask ApplyWeaponUpdate(NetPeer peer, int slotHash, string itemId, ItemSnapshot snapshot)
    {
        if (!remoteCharacters.TryGetValue(peer, out var remoteObj) || remoteObj == null) return;

        var cm = remoteObj.GetComponent<CharacterMainControl>();
        var model = cm ? cm.characterModel : null;
        if (model == null) return;

        // —— 幂等/去抖：同一 peer、同一槽、同一 item 在 200ms 内重复到达则忽略 ——
        var key = $"{peer?.Id ?? -1}:{slotHash}";
        var want = itemId ?? string.Empty;
        if (_lastWeaponAppliedByPeer.TryGetValue(key, out var last) &&
            last == want &&
            _lastWeaponAppliedTimeByPeer.TryGetValue(key, out var ts) &&
            Time.time - ts < WeaponApplyDebounce)
            return;
        _lastWeaponAppliedByPeer[key] = want;
        _lastWeaponAppliedTimeByPeer[key] = Time.time;

        var socket = CoopTool.ResolveSocketOrDefault(slotHash);

        try
        {
            var typeId = snapshot.TypeId;
            if (typeId == 0 && !string.IsNullOrEmpty(itemId) && int.TryParse(itemId, out var parsed))
                typeId = parsed;

            if (typeId != 0)
            {
                // 准备 Item，挂载前先杀残留 agent
                Item item = null;
                if (snapshot.TypeId != 0)
                    item = ItemTool.BuildItemFromSnapshot(snapshot);

                if (item == null)
                    item = await COOPManager.GetItemAsync(typeId);

                if (item != null)
                {

                   // cm.CharacterItem.TryPlug(item);
                    cm.ChangeHoldItem(item);
                    try
                    {
                        await UniTask.NextFrame(); // 让挂载真正生效，避免同帧取不到组件

                        var gun = model ? model.GetComponentInChildren<ItemAgent_Gun>(true) : null;
                        var mz = gun && gun.muzzle ? gun.muzzle : null;
                        if (!mz && model)
                        {
                            // 兜底从骨骼名找一下
                            var t = model.transform;
                            mz = t.Find("Muzzle") ??
                                 (model.RightHandSocket ? model.RightHandSocket.Find("Muzzle") : null) ??
                                 (model.LefthandSocket ? model.LefthandSocket.Find("Muzzle") : null) ??
                                 (model.MeleeWeaponSocket ? model.MeleeWeaponSocket.Find("Muzzle") : null);
                        }

                        if (playerStatuses.TryGetValue(peer, out var ps) && ps != null && !string.IsNullOrEmpty(ps.EndPoint) && gun)
                            LocalPlayerManager.Instance._gunCacheByShooter[ps.EndPoint] = (gun, mz);
                    }
                    catch
                    {
                    }

                    // 缓存弹丸和 muzzleFx
                    var gunSetting = item.GetComponent<ItemSetting_Gun>();
                    var pfb = gunSetting && gunSetting.bulletPfb
                        ? gunSetting.bulletPfb
                        : GameplayDataSettings.Prefabs.DefaultBullet;
                    LocalPlayerManager.Instance._projCacheByWeaponType[typeId] = pfb;
                    LocalPlayerManager.Instance._muzzleFxCacheByWeaponType[typeId] = gunSetting ? gunSetting.muzzleFxPfb : null;
                }
            }
            else
            {
                // 只清指定插槽
                CoopTool.ClearWeaponSlot(model, socket);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"更新武器失败(主机): {peer?.EndPoint}, Slot={socket}, ItemId={itemId}, 错误: {ex.Message}");
        }
    }

    public void PlayShootAnimOnServerPeer(NetPeer peer)
    {
        if (!remoteCharacters.TryGetValue(peer, out var who) || !who) return;
        var animCtrl = who.GetComponent<CharacterMainControl>().characterModel.GetComponentInParent<CharacterAnimationControl_MagicBlend>();
        if (animCtrl && animCtrl.animator) animCtrl.OnAttack(); // 这个控制器里会触发 Attack trigger + 攻击图层权重曲线
    }
}
