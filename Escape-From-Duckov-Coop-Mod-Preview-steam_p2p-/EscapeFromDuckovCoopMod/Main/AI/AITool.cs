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

using System.Reflection;
using Duckov;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using NodeCanvas.Framework;
using NodeCanvas.StateMachines;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public static class AITool
{
    private const float AUTOBIND_COOLDOWN = 0.20f; // 200ms：同一 aiId 的重试冷却
    private const float AUTOBIND_RADIUS = 35f; // 近场搜索半径，可按需要 25~40f
    private const QueryTriggerInteraction AUTOBIND_QTI = QueryTriggerInteraction.Collide;
    private const int AUTOBIND_LAYERMASK = ~0; // 如有专用 Layer，可替换~~~~~~oi
    private const QueryTriggerInteraction QTI = QueryTriggerInteraction.Collide;
    private const int LAYER_MASK_ANY = ~0;
    public static readonly Dictionary<int, CharacterMainControl> aiById = new();

    private static readonly Dictionary<int, int> _aiSerialPerRoot = new();
    public static bool _aiSceneReady;

    // —— AutoBind 限频/范围参数 —— 
    private static readonly Dictionary<int, float> _lastAutoBindTryTime = new();

    private static readonly Collider[] _corpseScanBuf = new Collider[64];

    public static readonly HashSet<int> _cliAiDeathFxOnce = new();
    private static NetService Service => NetService.Instance;
    private static bool IsServer => Service != null && Service.IsServer;
    private static NetManager netManager => Service?.netManager;
    private static NetDataWriter writer => Service?.writer;
    private static NetPeer connectedPeer => Service?.connectedPeer;
    private static PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private static bool networkStarted => Service != null && Service.networkStarted;
    private static Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private static Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private static Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    public static int StableRootId(CharacterSpawnerRoot r)
    {
        if (r == null) return 0;
        if (r.SpawnerGuid != 0) return r.SpawnerGuid;

        // 取 relatedScene（Init 会设置）；拿不到就退化为当前场景索引
        var sceneIndex = -1;
        try
        {
            var fi = typeof(CharacterSpawnerRoot).GetField("relatedScene", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null) sceneIndex = (int)fi.GetValue(r);
        }
        catch
        {
        }

        if (sceneIndex < 0) sceneIndex = SceneManager.GetActiveScene().buildIndex;

        // 世界坐标量化到 0.1m，避免浮点抖动
        var p = r.transform.position;
        var qx = Mathf.RoundToInt(p.x * 10f);
        var qy = Mathf.RoundToInt(p.y * 10f);
        var qz = Mathf.RoundToInt(p.z * 10f);

        // 名称 + 位置 + 场景索引 → FNV1a
        var key = $"{sceneIndex}:{r.name}:{qx},{qy},{qz}";
        return StableHash(key);
    }

    public static int StableRootId_Alt(CharacterSpawnerRoot r)
    {
        if (r == null) return 0;

        // 不看 SpawnerGuid，强制用 场景索引 + 名称 + 量化坐标
        var sceneIndex = -1;
        try
        {
            var fi = typeof(CharacterSpawnerRoot).GetField("relatedScene", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null) sceneIndex = (int)fi.GetValue(r);
        }
        catch
        {
        }

        if (sceneIndex < 0)
            sceneIndex = SceneManager.GetActiveScene().buildIndex;

        var p = r.transform.position;
        var qx = Mathf.RoundToInt(p.x * 10f);
        var qy = Mathf.RoundToInt(p.y * 10f);
        var qz = Mathf.RoundToInt(p.z * 10f);

        var key = $"{sceneIndex}:{r.name}:{qx},{qy},{qz}";
        return StableHash(key);
    }

    public static bool Client_ApplyAiAnim(int id, AiAnimState st)
    {
        if (aiById.TryGetValue(id, out var cmc) && cmc)
        {
            if (!IsRealAI(cmc)) return false; // 保险
            // 确保 AI 代理上有 NetAiFollower 与 RemoteReplicaTag（禁用本地 MagicBlend.Update）
            var follower = cmc.GetComponent<NetAiFollower>();
            if (!follower) follower = cmc.gameObject.AddComponent<NetAiFollower>();
            if (!cmc.GetComponent<RemoteReplicaTag>()) cmc.gameObject.AddComponent<RemoteReplicaTag>();

            follower.SetAnim(st.speed, st.dirX, st.dirY, st.hand, st.gunReady, st.dashing);
            return true;
        }

        return false;
    }

    public static bool IsRealAI(CharacterMainControl cmc)
    {
        if (cmc == null) return false;

        // 过滤主角
        if (cmc == CharacterMainControl.Main)
            return false;

        if (cmc.Team == Teams.player) return false;

        var lm = LevelManager.Instance;
        if (lm != null)
        {
            if (cmc == lm.PetCharacter) return false;
            if (lm.PetProxy != null && cmc.gameObject == lm.PetProxy.gameObject) return false;
        }

        // 过滤远程玩家（remoteCharacters 管理的对象）
        foreach (var go in remoteCharacters.Values)
            if (go != null && cmc.gameObject == go)
                return false;
        foreach (var go in clientRemoteCharacters.Values)
            if (go != null && cmc.gameObject == go)
                return false;

        return true;
    }

    public static CharacterMainControl TryAutoBindAi(int aiId, Vector3 snapPos)
    {
        var best = 30f; // 原 5f -> 放宽，必要时可调到 40f
        CharacterMainControl bestCmc = null;

        var all = Object.FindObjectsOfType<CharacterMainControl>(true);
        foreach (var c in all)
        {
            if (!c || LevelManager.Instance.MainCharacter == c) continue;
            if (aiById.ContainsValue(c)) continue;

            var a = new Vector2(c.transform.position.x, c.transform.position.z);
            var b = new Vector2(snapPos.x, snapPos.z);
            var d = Vector2.Distance(a, b);

            if (d < best)
            {
                best = d;
                bestCmc = c;
            }
        }

        if (bestCmc != null)
        {
            COOPManager.AIHandle.RegisterAi(aiId, bestCmc); // ↓ 第②步里我们会让 RegisterAi 在客户端同时挂 Follower
            if (COOPManager.AIHandle.freezeAI) TryFreezeAI(bestCmc);
        }

        return bestCmc;
    }


    public static void Client_ForceFreezeAllAI()
    {
        if (!networkStarted || IsServer) return;
        var all = Object.FindObjectsOfType<AICharacterController>(true);
        foreach (var aic in all)
        {
            if (!aic) continue;
            aic.enabled = false;
            var cmc = aic.GetComponentInParent<CharacterMainControl>();
            if (cmc) TryFreezeAI(cmc); // 会关 BehaviourTreeOwner + NavMeshAgent + AICtrl
        }
    }

    public static int NextAiSerial(int rootId)
    {
        if (!_aiSerialPerRoot.TryGetValue(rootId, out var n)) n = 0;
        n++;
        _aiSerialPerRoot[rootId] = n;
        return n;
    }

    public static void ResetAiSerials()
    {
        _aiSerialPerRoot.Clear();
    }

    public static void MarkAiSceneReady()
    {
        _aiSceneReady = true;
    }


    public static void ApplyAiTransform(int aiId, Vector3 p, Vector3 f)
    {
        if (!aiById.TryGetValue(aiId, out var cmc) || !cmc)
        {
            cmc = TryAutoBindAiWithBudget(aiId, p); // 新版：窄范围 + 限频
            if (!cmc) return; // 等下一帧
        }

        if (!IsRealAI(cmc)) return;

        var follower = cmc.GetComponent<NetAiFollower>() ?? cmc.gameObject.AddComponent<NetAiFollower>();
        follower.SetTarget(p, f);
    }

    public static CharacterMainControl TryAutoBindAiWithBudget(int aiId, Vector3 snapPos)
    {
        // 1) 限频：同一 aiId 在冷却期内直接跳过
        if (_lastAutoBindTryTime.TryGetValue(aiId, out var last) && Time.time - last < AUTOBIND_COOLDOWN)
            return null;
        _lastAutoBindTryTime[aiId] = Time.time;

        // 2) 近场搜索：用 OverlapSphere 缩小枚举规模
        CharacterMainControl best = null;
        var bestSqr = float.MaxValue;

        var cols = Physics.OverlapSphere(snapPos, AUTOBIND_RADIUS, AUTOBIND_LAYERMASK, AUTOBIND_QTI);
        for (var i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (!c) continue;

            var cmc = c.GetComponentInParent<CharacterMainControl>();
            if (!cmc) continue;
            if (LevelManager.Instance && LevelManager.Instance.MainCharacter == cmc) continue; // 跳过玩家本体
            if (!cmc.gameObject.activeInHierarchy) continue;
            if (!IsRealAI(cmc)) continue;

            if (aiById.ContainsValue(cmc)) continue; // 已被别的 aiId 占用

            var d2 = (cmc.transform.position - snapPos).sqrMagnitude;
            if (d2 < bestSqr)
            {
                bestSqr = d2;
                best = cmc;
            }
        }

        if (best != null)
        {
            if (!IsRealAI(best)) return null;

            COOPManager.AIHandle.RegisterAi(aiId, best); // 已有：登记 &（在客户端）自动挂 NetAiFollower
            if (COOPManager.AIHandle.freezeAI) TryFreezeAI(best); // 你已有的“冻结”辅助（可选）
            return best;
        }

        // 3) 罕见兜底：偶尔扫一次 NetAiTag 做精确匹配（低频触发）
        if (Time.frameCount % 20 == 0) // 大约每 20 帧才做一次全局查看
        {
            var tags = Object.FindObjectsOfType<NetAiTag>(true);
            for (var i = 0; i < tags.Length; i++)
            {
                var tag = tags[i];
                if (!tag || tag.aiId != aiId) continue;
                var cmc = tag.GetComponentInParent<CharacterMainControl>();
                if (cmc && !aiById.ContainsValue(cmc))
                {
                    COOPManager.AIHandle.RegisterAi(aiId, cmc);
                    if (COOPManager.AIHandle.freezeAI) TryFreezeAI(cmc);
                    return cmc;
                }
            }
        }

        return null; // 这帧没命中，就等下一帧/下一次快照
    }


    public static List<EquipmentSyncData> GetLocalAIEquipment(CharacterMainControl cmc)
    {
        var equipmentList = new List<EquipmentSyncData>();
        var equipmentController = cmc?.EquipmentController;
        if (equipmentController == null) return equipmentList;

        var slotNames = new[] { "armorSlot", "helmatSlot", "faceMaskSlot", "backpackSlot", "headsetSlot" };
        var slotHashes = new[]
        {
            CharacterEquipmentController.armorHash, CharacterEquipmentController.helmatHash, CharacterEquipmentController.faceMaskHash,
            CharacterEquipmentController.backpackHash, CharacterEquipmentController.headsetHash
        };

        for (var i = 0; i < slotNames.Length; i++)
            try
            {
                var slotField = Traverse.Create(equipmentController).Field<Slot>(slotNames[i]);
                if (slotField.Value == null) continue;

                var slot = slotField.Value;
                var itemId = slot?.Content != null ? slot.Content.TypeID.ToString() : "";
                equipmentList.Add(new EquipmentSyncData { SlotHash = slotHashes[i], ItemId = itemId });
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取槽位 {slotNames[i]} 时发生错误: {ex.Message}");
            }

        return equipmentList;
    }


    // —— 冻结AI（生成后一键静止）——
    public static void TryFreezeAI(CharacterMainControl cmc)
    {
        if (!cmc) return;

        if (!IsRealAI(cmc)) return;

        var all = Object.FindObjectsOfType<AICharacterController>(true);
        foreach (var aic in all)
        {
            if (!aic) continue;
            aic.enabled = false;
        }

        var all1 = Object.FindObjectsOfType<AI_PathControl>(true);
        foreach (var aic in all1)
        {
            if (!aic) continue;
            aic.enabled = false;
        }

        var all2 = Object.FindObjectsOfType<FSMOwner>(true);
        foreach (var aic in all2)
        {
            if (!aic) continue;
            aic.enabled = false;
        }

        var all3 = Object.FindObjectsOfType<Blackboard>(true);
        foreach (var aic in all3)
        {
            if (!aic) continue;
            aic.enabled = false;
        }
    }

    public static int StableHash(string s)
    {
        unchecked
        {
            var h = 2166136261;
            for (var i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619;
            }

            return (int)h;
        }
    }

    public static string TransformPath(Transform t)
    {
        var stack = new Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }

        return string.Join("/", stack);
    }

    public static int DeriveSeed(int a, int b)
    {
        unchecked
        {
            var h = 2166136261;
            h ^= (uint)a;
            h *= 16777619;
            h ^= (uint)b;
            h *= 16777619;
            return (int)h;
        }
    }

    public static void EnsureMagicBlendBound(CharacterMainControl cmc)
    {
        if (!cmc) return;
        var model = cmc.characterModel;
        if (!model) return;

        var blend = model.GetComponent<CharacterAnimationControl_MagicBlend>();
        if (!blend) blend = model.gameObject.AddComponent<CharacterAnimationControl_MagicBlend>();

        if (cmc.GetGun() != null)
        {
            blend.animator.SetBool(Animator.StringToHash("GunReady"), true);
            Traverse.Create(blend).Field<ItemAgent_Gun>("gunAgent").Value = cmc.GetGun();
            Traverse.Create(blend).Field<DuckovItemAgent>("holdAgent").Value = cmc.GetGun();
        }

        if (cmc.GetMeleeWeapon() != null)
        {
            blend.animator.SetBool(Animator.StringToHash("GunReady"), true);
            Traverse.Create(blend).Field<DuckovItemAgent>("holdAgent").Value = cmc.GetMeleeWeapon();
        }

        blend.characterModel = model;
        blend.characterMainControl = cmc;

        if (!blend.animator || blend.animator == null)
            blend.animator = model.GetComponentInChildren<Animator>(true);

        var anim = blend.animator;
        if (anim)
        {
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.updateMode = AnimatorUpdateMode.Normal;
            var idx = anim.GetLayerIndex("MeleeAttack");
            if (idx >= 0) anim.SetLayerWeight(idx, 0f);
        }
    }

    public static void TryClientRemoveNearestAICorpse(Vector3 pos, float radius)
    {
        if (!networkStarted || IsServer) return;

        try
        {
            CharacterMainControl best = null;
            var bestSqr = radius * radius;

            // 1) 先用你已有的 aiById 做 O(n) 精确筛选（不扫全场）
            try
            {
                foreach (var kv in aiById)
                {
                    var cmc = kv.Value;
                    if (!cmc || cmc.IsMainCharacter) continue;

                    var isAI = cmc.GetComponent<AICharacterController>() != null
                               || cmc.GetComponent<NetAiTag>() != null;
                    if (!isAI) continue;

                    var p = cmc.transform.position;
                    p.y = 0f;
                    var q = pos;
                    q.y = 0f;
                    var d2 = (p - q).sqrMagnitude;
                    if (d2 < bestSqr)
                    {
                        best = cmc;
                        bestSqr = d2;
                    }
                }
            }
            catch
            {
            }

            // 2) 仍未命中时，用“局部物理探测”替代全场景扫描（无 GC）
            if (!best)
            {
                var n = Physics.OverlapSphereNonAlloc(pos, radius, _corpseScanBuf, LAYER_MASK_ANY, QTI);
                for (var i = 0; i < n; i++)
                {
                    var c = _corpseScanBuf[i];
                    if (!c) continue;

                    var cmc = c.GetComponentInParent<CharacterMainControl>();
                    if (!cmc || cmc.IsMainCharacter) continue;

                    var isAI = cmc.GetComponent<AICharacterController>() != null
                               || cmc.GetComponent<NetAiTag>() != null;
                    if (!isAI) continue;

                    var p = cmc.transform.position;
                    p.y = 0f;
                    var q = pos;
                    q.y = 0f;
                    var d2 = (p - q).sqrMagnitude;
                    if (d2 < bestSqr)
                    {
                        best = cmc;
                        bestSqr = d2;
                    }
                }
            }


            if (best)
            {
                var DamageInfo = new DamageInfo
                {
                    armorBreak = 999f, damageValue = 9999f, fromWeaponItemID = CharacterMainControl.Main.CurrentHoldItemAgent.Item.TypeID,
                    damageType = DamageTypes.normal, fromCharacter = CharacterMainControl.Main, finalDamage = 9999f, toDamageReceiver = best.mainDamageReceiver
                };
                EXPManager.AddExp(Traverse.Create(best.Health).Field<Item>("item").Value.GetInt("Exp"));

                // 经验共享获取，共享击杀lol

                //best.Health.Hurt(DamageInfo);
                best.Health.OnDeadEvent.Invoke(DamageInfo);
                TryFireOnDead(best.Health, DamageInfo);

                try
                {
                    var tag = best.GetComponent<NetAiTag>();
                    if (tag != null)
                        if (_cliAiDeathFxOnce.Add(tag.aiId))
                            FxManager.Client_PlayAiDeathFxAndSfx(best);
                }
                catch
                {
                }

                Object.Destroy(best.gameObject);
            }
        }
        catch
        {
        }
    }

    public static bool TryFireOnDead(Health health, DamageInfo di)
    {
        try
        {
            // OnDead 是 static event<Action<Health, DamageInfo>>
            var fi = AccessTools.Field(typeof(Health), "OnDead");
            if (fi == null)
            {
                Debug.LogError("[HEALTH] 找不到 OnDead 字段（可能是自定义 add/remove 事件）");
                return false;
            }

            var del = fi.GetValue(null) as Action<Health, DamageInfo>;
            if (del == null)
                // 没有任何订阅者就不会触发
                return false;

            del.Invoke(health, di);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[HEALTH] 触发 OnDead 失败: " + e);
            return false;
        }
    }
}

public struct AiAnimState
{
    public float speed, dirX, dirY;
    public int hand;
    public bool gunReady, dashing;
}