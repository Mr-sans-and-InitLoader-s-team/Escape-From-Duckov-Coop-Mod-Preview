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
using Saves;
using UnityEngine.AI;

namespace EscapeFromDuckovCoopMod;

public static class CreateRemoteCharacter
{
    private static NetService Service => NetService.Instance;
    private static bool IsServer => Service != null && Service.IsServer;
    private static NetManager netManager => Service?.netManager;
    private static NetDataWriter writer => Service?.writer;
    private static NetPeer connectedPeer => Service?.connectedPeer;

    // 日志频率限制
    private static int _createRemoteLogCount = 0;
    private static System.DateTime _lastCreateRemoteLogTime = System.DateTime.MinValue;
    private const double CREATE_REMOTE_LOG_INTERVAL = 5.0;
    private static PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private static bool networkStarted => Service != null && Service.networkStarted;
    private static Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private static Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private static Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    public static async UniTask<GameObject> CreateRemoteCharacterAsync(NetPeer peer, Vector3 position, Quaternion rotation, string customFaceJson)
    {
        if (remoteCharacters.ContainsKey(peer) && remoteCharacters[peer] != null) return null;

        var levelManager = LevelManager.Instance;
        if (levelManager == null || levelManager.MainCharacter == null) return null;

        var instance = GameObject.Instantiate(CharacterMainControl.Main.gameObject, position, rotation);
        // ✅ 优化：复用组件引用，避免重复 GetComponent
        var characterModel = instance.GetComponent<CharacterMainControl>();

        //  cInventory = CharacterMainControl.Main.CharacterItem.Inventory;
        //  Traverse.Create(characterModel.CharacterItem).Field<Inventory>("inventory").Value = cInventory;

        COOPManager.StripAllHandItems(characterModel);
        var itemLoaded = await ItemSavesUtilities.LoadItem(LevelManager.MainCharacterItemSaveKey);
        if (itemLoaded == null)
        {
            itemLoaded = await ItemAssetsCollection.InstantiateAsync(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
            Debug.LogWarning("Item Loading failed");
        }

        Traverse.Create(characterModel).Field<Item>("characterItem").Value = itemLoaded;
        // Debug.Log(peer.EndPoint.ToString() + " CreateRemoteCharacterForClient");
        // 统一设置初始位姿
        instance.transform.SetPositionAndRotation(position, rotation);

        MakeRemotePhysicsPassive(instance);

        CustomFace.StripAllCustomFaceParts(instance);

        if (characterModel?.characterModel.CustomFace != null && !string.IsNullOrEmpty(customFaceJson))
        {
            var customFaceData = JsonUtility.FromJson<CustomFaceSettingData>(customFaceJson);
            characterModel.characterModel.CustomFace.LoadFromData(customFaceData);
        }

        try
        {
            var cm = characterModel.characterModel;

            COOPManager.ChangeArmorModel(cm, null);
            COOPManager.ChangeHelmatModel(cm, null);
            COOPManager.ChangeFaceMaskModel(cm, null);
            COOPManager.ChangeBackpackModel(cm, null);
            COOPManager.ChangeHeadsetModel(cm, null);
        }
        catch
        {
        }


        instance.AddComponent<RemoteReplicaTag>();
        var anim = instance.GetComponentInChildren<Animator>(true);
        if (anim)
        {
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.updateMode = AnimatorUpdateMode.Normal;
        }

        var h = instance.GetComponentInChildren<Health>(true);
        if (h) h.autoInit = false; // ★ 阻止 Start()->Init() 把血直接回满
        instance.AddComponent<AutoRequestHealthBar>(); // 你已有就不要重复
        // 主机创建完后立刻挂监听并推一次
        HealthTool.Server_HookOneHealth(peer, instance);
        instance.AddComponent<HostForceHealthBar>();

        NetInterpUtil.Attach(instance)?.Push(position, rotation);
        AnimInterpUtil.Attach(instance); // 先挂上，样本由后续网络包填
        characterModel.gameObject.SetActive(false);
        remoteCharacters[peer] = instance;
        characterModel.gameObject.SetActive(true);

        // 🕐 标记玩家已成功进入游戏，清除加入超时计时
        Service.MarkPlayerJoinedSuccessfully(peer);

        return instance;
    }

    public static async UniTask CreateRemoteCharacterForClient(string playerId, Vector3 position, Quaternion rotation, string customFaceJson)
    {
        if (NetService.Instance.IsSelfId(playerId)) return; // ★ 不给自己创建"远程自己"
        if (clientRemoteCharacters.ContainsKey(playerId) && clientRemoteCharacters[playerId] != null) return;

        // 频率限制：避免刷屏
        _createRemoteLogCount++;
        var now = System.DateTime.Now;
        if ((now - _lastCreateRemoteLogTime).TotalSeconds >= CREATE_REMOTE_LOG_INTERVAL)
        {
            if (_createRemoteLogCount > 1)
            {
                Debug.Log($"[CreateRemote] 创建了 {_createRemoteLogCount} 个远程角色 (最后: {playerId})");
            }
            else
            {
                Debug.Log($"[CreateRemote] {playerId} CreateRemoteCharacterForClient");
            }
            _createRemoteLogCount = 0;
            _lastCreateRemoteLogTime = now;
        }

        var levelManager = LevelManager.Instance;
        if (levelManager == null || levelManager.MainCharacter == null) return;


        var instance = GameObject.Instantiate(CharacterMainControl.Main.gameObject, position, rotation);
        // ✅ 优化：复用组件引用，避免重复 GetComponent
        var characterModel = instance.GetComponent<CharacterMainControl>();

        var itemLoaded = await ItemSavesUtilities.LoadItem(LevelManager.MainCharacterItemSaveKey);
        if (itemLoaded == null) itemLoaded = await ItemAssetsCollection.InstantiateAsync(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
        Traverse.Create(characterModel).Field<Item>("characterItem").Value = itemLoaded;

        COOPManager.StripAllHandItems(characterModel);

        instance.transform.SetPositionAndRotation(position, rotation);

        // ✅ 优化：复用 characterModel，GetComponentInChildren 在此场景下返回同一对象
        if (characterModel && characterModel.modelRoot)
        {
            var e = rotation.eulerAngles;
            characterModel.modelRoot.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
        }

        MakeRemotePhysicsPassive(instance);
        CustomFace.StripAllCustomFaceParts(instance);

        // 如果入参为空，尽量从已知状态或待应用表拿，再应用（允许为空；为空时后续状态更新会补）
        if (string.IsNullOrEmpty(customFaceJson))
        {
            if (NetService.Instance.clientPlayerStatuses.TryGetValue(playerId, out var st) && !string.IsNullOrEmpty(st.CustomFaceJson))
                customFaceJson = st.CustomFaceJson;
            else if (CustomFace._cliPendingFace.TryGetValue(playerId, out var pending) && !string.IsNullOrEmpty(pending))
                customFaceJson = pending;
        }


        CustomFace.Client_ApplyFaceIfAvailable(playerId, instance, customFaceJson);


        try
        {
            var cm = characterModel.characterModel;

            COOPManager.ChangeArmorModel(cm, null);
            COOPManager.ChangeHelmatModel(cm, null);
            COOPManager.ChangeFaceMaskModel(cm, null);
            COOPManager.ChangeBackpackModel(cm, null);
            COOPManager.ChangeHeadsetModel(cm, null);
        }
        catch
        {
        }

        instance.AddComponent<RemoteReplicaTag>();
        var anim = instance.GetComponentInChildren<Animator>(true);
        if (anim)
        {
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.updateMode = AnimatorUpdateMode.Normal;
        }

        var h = instance.GetComponentInChildren<Health>(true);
        if (h) h.autoInit = false;
        instance.AddComponent<AutoRequestHealthBar>();
        CoopTool.Client_ApplyPendingRemoteIfAny(playerId, instance);

        NetInterpUtil.Attach(instance)?.Push(position, rotation);
        AnimInterpUtil.Attach(instance);
        characterModel.gameObject.SetActive(false);
        clientRemoteCharacters[playerId] = instance;
        characterModel.gameObject.SetActive(true);
    }

    private static void MakeRemotePhysicsPassive(GameObject go)
    {
        if (!go) return;

        // 1) 典型运动/导航组件：关掉使其不再自行挪动
        var ai = go.GetComponentInChildren<AICharacterController>(true);
        if (ai) ai.enabled = false;

        var nma = go.GetComponentInChildren<NavMeshAgent>(true);
        if (nma) nma.enabled = false;

        var cc = go.GetComponentInChildren<CharacterController>(true);
        if (cc) cc.enabled = false; // 命中体积通常有独立 collider，不依赖 CC

        // 2) 刚体改为运动由我们驱动
        var rb = go.GetComponentInChildren<Rigidbody>(true);
        if (rb)
        {
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 3) 确保 Animator 不做 root motion（动画仍会更新）
        var anim = go.GetComponentInChildren<Animator>(true);
        if (anim) anim.applyRootMotion = false;

        // 其它你项目里会“推进角色”的脚本，可按名称做兜底反射关闭
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!mb) continue;
            var n = mb.GetType().Name;
            // 只关闭明显与移动/导航相关的
            if (n.Contains("Locomotion") || n.Contains("Movement") || n.Contains("Motor"))
            {
                var beh = mb as Behaviour;
                if (beh) beh.enabled = false;
            }
        }
    }
}