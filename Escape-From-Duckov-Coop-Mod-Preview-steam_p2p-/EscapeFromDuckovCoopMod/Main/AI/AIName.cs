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

using System.Collections;
using Duckov.UI;
using Duckov.Utilities;
using TMPro;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

public static class AIName
{
    private static readonly Dictionary<int, string> _aiFaceJsonById = new();


    public static readonly HashSet<int> _nameIconSealed = new();

    // —— 主机：icon 为空时，延迟一次复查并重播 —— 
    public static readonly HashSet<int> _iconRebroadcastScheduled = new();


    public static readonly AccessTools.FieldRef<CharacterRandomPreset, CharacterIconTypes>
        FR_IconType = AccessTools.FieldRefAccess<CharacterRandomPreset, CharacterIconTypes>("characterIconType");

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

    public static string NormalizePrefabName(string n)
    {
        if (string.IsNullOrEmpty(n)) return n;
        n = n.Trim();
        const string clone = "(Clone)";
        if (n.EndsWith(clone)) n = n.Substring(0, n.Length - clone.Length).Trim();
        return n;
    }

    public static CharacterModel FindCharacterModelByName_Any(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        name = NormalizePrefabName(name);

        // A. 从所有已加载的“资源对象”里找 prefab（不隶属任何 Scene）
        //    注意：FindObjectsOfTypeAll 能拿到隐藏对象/资产，但要过滤掉场景实例
        foreach (var m in Resources.FindObjectsOfTypeAll<CharacterModel>())
        {
            if (!m) continue;
            if (m.gameObject.scene.IsValid()) continue; // 这是场景实例，跳过
            if (string.Equals(NormalizePrefabName(m.name), name, StringComparison.OrdinalIgnoreCase))
                return m;
        }

        // B. 再从 Resources 目录里加载一次（项目若没用 Addressables，这步很有用）
        try
        {
            foreach (var m in Resources.LoadAll<CharacterModel>(""))
            {
                if (!m) continue;
                if (string.Equals(NormalizePrefabName(m.name), name, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
        }
        catch
        {
            /* 项目可能没放 Resources；忽略 */
        }

        // C. 最后才扫描场景中的“已存在实例”（极端兜底）
        foreach (var m in GameObject.FindObjectsOfType<CharacterModel>())
        {
            if (!m) continue;
            if (string.Equals(NormalizePrefabName(m.name), name, StringComparison.OrdinalIgnoreCase))
                return m;
        }

        return null;
    }

    public static void ReapplyFaceIfKnown(CharacterMainControl cmc)
    {
        if (!cmc || IsServer) return;
        var aiId = -1;
        foreach (var kv in AITool.aiById)
            if (kv.Value == cmc)
            {
                aiId = kv.Key;
                break;
            }

        if (aiId < 0) return;

        if (_aiFaceJsonById.TryGetValue(aiId, out var json) && !string.IsNullOrEmpty(json))
            CustomFace.ApplyFaceJsonToModel(cmc.characterModel, json);
    }

    // 进入新关卡时清空封存（你已有 Level 初始化回调就放那里）
    public static void Client_ResetNameIconSeal_OnLevelInit()
    {
        if (!IsServer) _nameIconSealed.Clear();
        if (IsServer) return;
        foreach (var tag in GameObject.FindObjectsOfType<NetAiTag>())
        {
            var cmc = tag ? tag.GetComponent<CharacterMainControl>() : null;
            if (!cmc)
            {
                GameObject.Destroy(tag);
                continue;
            }

            if (!AITool.IsRealAI(cmc)) GameObject.Destroy(tag);
        }
    }

    public static Sprite ResolveIconSprite(int iconType)
    {
        switch ((CharacterIconTypes)iconType)
        {
            case CharacterIconTypes.none: return null;
            case CharacterIconTypes.elete: return GameplayDataSettings.UIStyle.EleteCharacterIcon;
            case CharacterIconTypes.pmc: return GameplayDataSettings.UIStyle.PmcCharacterIcon;
            case CharacterIconTypes.boss: return GameplayDataSettings.UIStyle.BossCharacterIcon;
            case CharacterIconTypes.merchant: return GameplayDataSettings.UIStyle.MerchantCharacterIcon;
            case CharacterIconTypes.pet: return GameplayDataSettings.UIStyle.PetCharacterIcon;
            default: return null;
        }
    }

    // —— 客户端：确保血条后，多帧重试刷新图标与名字 —— 
    public static async UniTask RefreshNameIconWithRetries(CharacterMainControl cmc, int iconType, bool showName, string displayNameFromHost)
    {
        if (!cmc) return;

        //global::Duckov.UI.HealthBar hb1 = null;
        //foreach (var kv in aiById)
        //{
        //    if (kv.Value != null)
        //    {

        //        var preset = kv.Value.characterPreset;
        //        if (preset)
        //        {
        //            try { FR_IconType(preset) = (global::CharacterIconTypes)iconType; } catch { }
        //            try { preset.showName = showName; } catch { }
        //            try { Traverse.Create(preset).Field<string>("nameKey").Value = displayNameFromHost ?? string.Empty; } catch { }
        //        }

        //        var h1 = cmc.Health;
        //        if (h1 != null)
        //        {
        //            MethodInfo miGet1 = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(global::Health) });
        //            MethodInfo miRefresh1 = AccessTools.DeclaredMethod(typeof(global::Duckov.UI.HealthBar), "RefreshCharacterIcon", Type.EmptyTypes);
        //            if (miGet1 != null && HealthBarManager.Instance != null && h1 != null)
        //                hb1 = (global::Duckov.UI.HealthBar)miGet1.Invoke(HealthBarManager.Instance, new object[] { h1 });

        //            Traverse.Create(hb1).Field<Image>("levelIcon").Value.sprite = null;
        //            Traverse.Create(hb1).Field<TextMeshProUGUI>("nameText").Value.text = "";

        //            Traverse.Create(hb1).Field<Image>("levelIcon").Value.gameObject.SetActive(false);
        //            Traverse.Create(hb1).Field<TextMeshProUGUI>("nameText").Value.gameObject.SetActive(false);

        //            var tag = cmc.GetComponent<NetAiTag>() ?? cmc.gameObject.AddComponent<NetAiTag>();
        //            tag.iconTypeOverride = null;
        //            tag.showNameOverride = showName
        //                || ((CharacterIconTypes)iconType == CharacterIconTypes.boss
        //                  || (CharacterIconTypes)iconType == CharacterIconTypes.elete);
        //            tag.nameOverride = string.Empty;

        //            if (hb1 != null)
        //            {
        //                miRefresh1?.Invoke(hb1, null);
        //                continue;
        //            }
        //        }
        //        else
        //        {
        //            continue;
        //        }
        //    }
        //}

        try
        {
            var preset = cmc.characterPreset;
            if (preset)
            {
                try
                {
                    FR_IconType(preset) = (CharacterIconTypes)iconType;
                }
                catch
                {
                }

                try
                {
                    preset.showName = showName;
                }
                catch
                {
                }

                try
                {
                    Traverse.Create(preset).Field<string>("nameKey").Value = displayNameFromHost ?? string.Empty;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        // 2) 确保血条被请求并生成（已有 EnsureBarRoutine 可复用）
        var h = cmc.Health;

        // 3) 多帧重试拿 HealthBar 并调用私有 RefreshCharacterIcon()
        var miGet = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(Health) });
        var miRefresh = AccessTools.DeclaredMethod(typeof(HealthBar), "RefreshCharacterIcon", Type.EmptyTypes);

        HealthBar hb = null;
        for (var i = 0; i < 30; i++) // 最多重试 ~30 帧
            try
            {
                if (miGet != null && HealthBarManager.Instance != null && h != null)
                    hb = (HealthBar)miGet.Invoke(HealthBarManager.Instance, new object[] { h });

                Traverse.Create(hb).Field<Image>("levelIcon").Value.gameObject.SetActive(true);
                Traverse.Create(hb).Field<TextMeshProUGUI>("nameText").Value.gameObject.SetActive(true);

                Traverse.Create(hb).Field<Image>("levelIcon").Value.sprite = ResolveIconSprite(iconType);
                Traverse.Create(hb).Field<TextMeshProUGUI>("nameText").Value.text = displayNameFromHost;

                if (hb != null)
                {
                    var tag = cmc.GetComponent<NetAiTag>() ?? cmc.gameObject.AddComponent<NetAiTag>();
                    tag.iconTypeOverride = iconType;
                    tag.showNameOverride = showName
                                           || (CharacterIconTypes)iconType == CharacterIconTypes.boss
                                           || (CharacterIconTypes)iconType == CharacterIconTypes.elete;
                    tag.nameOverride = displayNameFromHost ?? string.Empty;

                    Debug.Log(
                        $"[AI_icon_Name 10s] {cmc.GetComponent<NetAiTag>().aiId} {cmc.characterPreset.Name} {cmc.characterPreset.GetCharacterIcon().name}");
                    break; // 成功一次即可
                }
            }
            catch
            {
            }
    }


    public static IEnumerator IconRebroadcastRoutine(int aiId, CharacterMainControl cmc)
    {
        yield return new WaitForSeconds(0.6f); // 等 UIStyle/预设就绪

        try
        {
            if (!IsServer || !cmc) yield break;

            var pr = cmc.characterPreset;
            var iconType = 0;
            var showName = false;

            if (pr)
            {
                try
                {
                    iconType = (int)FR_IconType(pr);
                }
                catch
                {
                }

                try
                {
                    // 运行期很多预设会把 icon 补上：再试一次
                    if (iconType == 0 && pr.GetCharacterIcon() != null)
                        iconType = (int)FR_IconType(pr);
                }
                catch
                {
                }
            }

            var e = (CharacterIconTypes)iconType;
            if (e == CharacterIconTypes.boss || e == CharacterIconTypes.elete)
                showName = true;

            // 现在拿到非 none 或识别为特殊类型，就再广播一遍
            if (iconType != 0 || showName)
                Server_BroadcastAiNameIcon(aiId, cmc);
        }
        finally
        {
            _iconRebroadcastScheduled.Remove(aiId);
        }
    }

    private static void Server_PeriodicNameIconSync()
    {
        foreach (var kv in AITool.aiById) // aiId -> cmc
        {
            var aiId = kv.Key;
            var cmc = kv.Value;
            if (!cmc) continue;

            var pr = cmc.characterPreset;
            if (!pr) continue;

            var iconType = 0;
            var showName = false;

            try
            {
                iconType = (int)FR_IconType(pr);
            }
            catch
            {
            }

            try
            {
                showName = pr.showName;
            }
            catch
            {
            }

            var e = (CharacterIconTypes)iconType;
            // 老规矩：boss/elete 强制显示名字，避免客户端再兜一次
            if (!showName && (e == CharacterIconTypes.boss || e == CharacterIconTypes.elete))
                showName = true;

            // 仅当“有图标”或“需要显示名字”时才重播，避免无意义带宽
            if (e != CharacterIconTypes.none || showName)
            {
                Debug.Log($"[AI-REBROADCAST-10s] aiId={aiId} icon={e} showName={showName}");
                COOPManager.AIHandle.Server_BroadcastAiLoadout(aiId, cmc); // 你现有的方法，里头会把名字一起下发
            }
        }
    }

    // 客户端：强制让血条读 preset 并刷新一次名字/图标
    public static void Client_PeriodicNameIconRefresh()
    {
        foreach (var kv in AITool.aiById)
        {
            var cmc = kv.Value;
            if (!cmc) continue;

            var pr = cmc.characterPreset;
            if (!pr) continue;

            var iconType = 0;
            var showName = false;
            string displayName = null;

            try
            {
                iconType = (int)FR_IconType(pr);
            }
            catch
            {
            }

            try
            {
                showName = pr.showName;
            }
            catch
            {
            }

            try
            {
                displayName = pr.DisplayName;
            }
            catch
            {
            }

            var e = (CharacterIconTypes)iconType;
            if (!showName && (e == CharacterIconTypes.boss || e == CharacterIconTypes.elete))
                showName = true;

            // 仅刷新“有图标或需要显示名字”的对象，避免白做工
            if (e == CharacterIconTypes.none && !showName) continue;

            // 利用你现有的多帧兜底：确保拿到 HealthBar 后反射调私有 RefreshCharacterIcon()
            RefreshNameIconWithRetries(cmc, iconType, showName, displayName).Forget();
        }
    }

    public static void Server_BroadcastAiNameIcon(int aiId, CharacterMainControl cmc)
    {
        if (!networkStarted || !IsServer || aiId == 0 || !cmc) return;

        var iconType = 0;
        var showName = false;
        string displayName = null;

        try
        {
            var pr = cmc.characterPreset;
            if (pr)
            {
                // 读取/兜底 iconType
                try
                {
                    iconType = (int)FR_IconType(pr);
                }
                catch
                {
                }

                try
                {
                    if (iconType == 0 && pr.GetCharacterIcon() != null) // 运行期补上后的兜底
                        iconType = (int)FR_IconType(pr);
                }
                catch
                {
                }

                // showName 按预设 + 特殊类型强制
                try
                {
                    showName = pr.showName;
                }
                catch
                {
                }

                var e = (CharacterIconTypes)iconType;
                if (!showName && (e == CharacterIconTypes.boss || e == CharacterIconTypes.elete))
                    showName = true;

                // 名字文本用主机裁决（你之前就是从 preset.Name 拿的）
                try
                {
                    displayName = pr.Name;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        Debug.Log($"[Server AIIcon_Name 10s] AI:{aiId} {cmc.characterPreset.Name} Icon{FR_IconType(cmc.characterPreset)}");
        var w = new NetDataWriter();
        w.Put((byte)Op.AI_NAME_ICON);
        w.Put(aiId);
        w.Put(iconType);
        w.Put(showName);
        w.Put(!string.IsNullOrEmpty(displayName));
        if (!string.IsNullOrEmpty(displayName)) w.Put(displayName);

        CoopTool.BroadcastReliable(w);
    }
}