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

using System.Collections.Generic;

namespace EscapeFromDuckovCoopMod.Utils;

/// <summary>
/// 场景名称映射工具 - 将场景ID映射为中文名称
/// </summary>
public static class SceneNameMapper
{
    /// <summary>
    /// 场景ID到中文名称的映射表
    /// </summary>
    private static readonly Dictionary<string, string> SceneNames = new()
    {
        // 基础场景
        { "Base", "基地" },
        
        // 海关系列
        { "Custom", "海关" },
        { "Custom_01", "海关1" },
        { "Custom_02", "海关2" },
        { "Custom_03", "海关3" },
        { "Custom_04", "海关4" },
        { "Custom_05", "海关5" },
        
        // 工厂系列
        { "Factory", "工厂" },
        { "Factory_01", "工厂1" },
        { "Factory_02", "工厂2" },
        { "Factory_03", "工厂3" },
        { "Factory_04", "工厂4" },
        
        // 村庄系列
        { "Village", "村庄" },
        { "Village_01", "村庄1" },
        
        // 零号区
        { "GroundZero", "零号区" },
        
        // 特殊
        { "Any", "任意地图" },
    };

    /// <summary>
    /// 获取场景的中文名称
    /// </summary>
    /// <param name="sceneId">场景ID（如 "Factory", "Custom_01"）</param>
    /// <returns>中文名称，如果未找到则返回原始ID</returns>
    public static string GetChineseName(string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId))
            return "未知场景";

        // 尝试从映射表获取
        if (SceneNames.TryGetValue(sceneId, out var chineseName))
            return chineseName;

        // 如果没有找到，返回原始ID
        return sceneId;
    }

    /// <summary>
    /// 获取场景的显示名称（优先使用游戏内置的DisplayName，如果是英文则替换为中文）
    /// </summary>
    /// <param name="sceneId">场景ID</param>
    /// <returns>显示名称</returns>
    public static string GetDisplayName(string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId))
            return "未知场景";

        // 尝试从游戏的SceneInfoCollection获取
        var sceneInfo = SceneInfoCollection.GetSceneInfo(sceneId);
        if (sceneInfo != null)
        {
            var displayName = sceneInfo.DisplayName;
            
            // 如果DisplayName是英文或与ID相同，使用我们的中文映射
            if (string.IsNullOrEmpty(displayName) || displayName == sceneId || IsEnglishName(displayName))
            {
                return GetChineseName(sceneId);
            }
            
            // 否则使用游戏内置的名称（可能已经是中文）
            return displayName;
        }

        // 如果游戏数据中没有，使用我们的映射
        return GetChineseName(sceneId);
    }

    /// <summary>
    /// 判断是否是英文名称（简单判断：只包含ASCII字符）
    /// </summary>
    private static bool IsEnglishName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return true;

        foreach (char c in name)
        {
            // 如果包含非ASCII字符（如中文），则不是纯英文
            if (c > 127)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 获取所有已知的场景ID列表
    /// </summary>
    public static IEnumerable<string> GetAllSceneIds()
    {
        return SceneNames.Keys;
    }

    /// <summary>
    /// 获取所有场景的中文名称列表
    /// </summary>
    public static IEnumerable<string> GetAllChineseNames()
    {
        return SceneNames.Values;
    }
}
