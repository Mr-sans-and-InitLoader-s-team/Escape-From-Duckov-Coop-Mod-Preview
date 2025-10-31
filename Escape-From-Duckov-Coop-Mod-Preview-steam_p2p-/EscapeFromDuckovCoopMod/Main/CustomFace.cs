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

using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public static class CustomFace
{
    // 客户端：远端玩家待应用的外观缓存
    public static readonly Dictionary<string, string> _cliPendingFace = new();
    private static NetService Service => NetService.Instance;

    //补充外观
    public static void Client_ApplyFaceIfAvailable(string playerId, GameObject instance, string faceOverride = null)
    {
        try
        {
            // 先挑一个 JSON
            var face = faceOverride;
            if (string.IsNullOrEmpty(face))
            {
                if (_cliPendingFace.TryGetValue(playerId, out var pf) && !string.IsNullOrEmpty(pf))
                    face = pf;
                else if (NetService.Instance.clientPlayerStatuses.TryGetValue(playerId, out var st) && !string.IsNullOrEmpty(st.CustomFaceJson))
                    face = st.CustomFaceJson;
            }

            // 没 JSON 就先不涂，等后续状态更新再补
            if (string.IsNullOrEmpty(face))
                return;

            // 反序列化成结构体（struct 永远非 null）
            var data = JsonUtility.FromJson<CustomFaceSettingData>(face);

            // 找到 CustomFaceInstance 并应用
            var cm = instance != null ? instance.GetComponentInChildren<CharacterModel>(true) : null;
            var cf = cm != null ? cm.CustomFace : null;
            if (cf != null)
            {
                HardApplyCustomFace(cf, data);
                _cliPendingFace[playerId] = face; // 记住成功涂过的 JSON
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[COOP][FACE] Apply failed for {playerId}: {e}");
        }
    }

    public static void HardApplyCustomFace(CustomFaceInstance cf, in CustomFaceSettingData data)
    {
        if (cf == null) return;
        try
        {
            StripAllCustomFaceParts(cf.gameObject);
        }
        catch
        {
        }

        try
        {
            cf.LoadFromData(data);
        }
        catch
        {
        }

        try
        {
            cf.RefreshAll();
        }
        catch
        {
        }
    }

    public static void StripAllCustomFaceParts(GameObject root)
    {
        try
        {
            var all = root.GetComponentsInChildren<CustomFacePart>(true);
            var n = 0;
            foreach (var p in all)
            {
                if (!p) continue;
                n++;
                Object.Destroy(p.gameObject);
            }

            Debug.Log($"[COOP][FACE] stripped {n} CustomFacePart");
        }
        catch
        {
        }
    }

    public static string LoadLocalCustomFaceJson()
    {
        try
        {
            string json = null;

            // 1) 尝试：LevelManager 的保存数据（struct，无需判 null）
            var lm = LevelManager.Instance;
            if (lm != null && lm.CustomFaceManager != null)
                try
                {
                    var data1 = lm.CustomFaceManager.LoadMainCharacterSetting(); // struct
                    json = JsonUtility.ToJson(data1);
                }
                catch
                {
                }

            // 2) 兜底：从运行时模型抓当前脸（ConvertToSaveData）
            if (string.IsNullOrEmpty(json) || json == "{}")
                try
                {
                    var main = CharacterMainControl.Main;
                    var model = main != null ? main.characterModel : null;
                    var cf = model != null ? model.CustomFace : null;
                    if (cf != null)
                    {
                        var data2 = cf.ConvertToSaveData(); // struct
                        var j2 = JsonUtility.ToJson(data2);
                        if (!string.IsNullOrEmpty(j2) && j2 != "{}")
                            json = j2;
                    }
                }
                catch
                {
                }

            // 3) 记住最近一次非空
            if (!string.IsNullOrEmpty(json) && json != "{}")
                LoaclPlayerManager.Instance._lastGoodFaceJson = json;

            // 4) 返回永不为空（尽量用缓存兜底）
            return !string.IsNullOrEmpty(json) && json != "{}" ? json : LoaclPlayerManager.Instance._lastGoodFaceJson ?? "";
        }
        catch
        {
            return LoaclPlayerManager.Instance._lastGoodFaceJson ?? "";
        }
    }

    // ---------- 工具：把主机下发的脸 JSON 套到模型 ----------
    public static void ApplyFaceJsonToModel(CharacterModel model, string faceJson)
    {
        if (model == null || string.IsNullOrEmpty(faceJson)) return;
        try
        {
            CustomFaceSettingData data;
            var ok = CustomFaceSettingData.JsonToData(faceJson, out data);
            if (!ok) data = JsonUtility.FromJson<CustomFaceSettingData>(faceJson);
            model.SetFaceFromData(data);
        }
        catch
        {
            /* 忽略异常避免中断 */
        }
    }
}