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

public class HostPlayerApply
{
    private const float WeaponApplyDebounce = 0.20f; // 200ms 去抖窗口

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
                Debug.Log($"尝试更新装备: {peer.EndPoint}, Slot={slotHash}, ItemId={itemId}");
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
            Debug.LogError($"更新装备失败(主机): {peer.EndPoint}, SlotHash={slotHash}, ItemId={itemId}, 错误: {ex.Message}");
        }
    }

    public async UniTask ApplyWeaponUpdate(NetPeer peer, int slotHash, string itemId)
    {
        if (!remoteCharacters.TryGetValue(peer, out var remoteObj) || remoteObj == null) return;

        var cm = remoteObj.GetComponent<CharacterMainControl>();
        var model = cm ? cm.characterModel : null;
        if (model == null) return;

        // —— 幂等/去抖：同一 peer、同一槽、同一 item 在 200ms 内重复到达则忽略 ——
        var key = $"{peer?.Id ?? -1}:{slotHash}";
        var want = itemId ?? string.Empty;
        if (_lastWeaponAppliedByPeer.TryGetValue(key, out var last) && last == want)
            // 同值重复，直接跳过
            return;
        if (_lastWeaponAppliedTimeByPeer.TryGetValue(key, out var ts))
            if (Time.time - ts < WeaponApplyDebounce && last == want)
                return;
        _lastWeaponAppliedByPeer[key] = want;
        _lastWeaponAppliedTimeByPeer[key] = Time.time;

        var socket = CoopTool.ResolveSocketOrDefault(slotHash);

        try
        {
            if (!string.IsNullOrEmpty(itemId) && int.TryParse(itemId, out var typeId))
            {
                // 准备 Item，挂载前先杀残留 agent
                var item = await COOPManager.GetItemAsync(typeId);
                if (item != null)
                {
                    CoopTool.SafeKillItemAgent(item);

                    // 只清目标插槽，避免多余的三处全清
                    CoopTool.ClearWeaponSlot(model, socket);

                    // 等一帧让销毁真正完成，避免“已有 agent”撞车
                    await UniTask.NextFrame();

                    // 挂载目标
                    COOPManager.ChangeWeaponModel(model, item, socket);

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
                            LoaclPlayerManager.Instance._gunCacheByShooter[ps.EndPoint] = (gun, mz);
                    }
                    catch
                    {
                    }

                    // 缓存弹丸和 muzzleFx
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