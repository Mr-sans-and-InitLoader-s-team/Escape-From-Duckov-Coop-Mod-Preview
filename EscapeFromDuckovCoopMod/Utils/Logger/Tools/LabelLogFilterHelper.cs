using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using EscapeFromDuckovCoopMod.Utils.Logger.LogFilters;
using EscapeFromDuckovCoopMod.Utils.Logger.Logs;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

namespace EscapeFromDuckovCoopMod.Utils.Logger.Tools
{
    public class LabelLogFilterHelper
    {
        private LabelLogFilterData filterData;

        private LabelLogFilterHelper() { }

#if UNITY_EDITOR
        private static Dictionary<string, LabelLogFilterHelper> helperDic;
#endif

        [Conditional("UNITY_EDITOR")]
        public static void RegisterToFilter(string name, LogFilter logFilter)
        {
#if UNITY_EDITOR
            logFilter.AddFilter<LabelLog>(GetFilterHelperInstance(name).CheckDebugLabel);
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public static void RegisterToFilter(string name, LogFilter<LabelLog> logFilter)
        {
#if UNITY_EDITOR
            logFilter.AddFilter(GetFilterHelperInstance(name).CheckDebugLabel);
#endif
        }

#if UNITY_EDITOR
        private static LabelLogFilterHelper GetFilterHelperInstance(string name)
        {
            if (helperDic == null)
            {
                helperDic = new Dictionary<string, LabelLogFilterHelper>();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = nameof(LabelLogFilterHelper);
            }

            LabelLogFilterHelper helper = null;

            if (helperDic.TryGetValue(name, out helper))
            {
                return helper;
            }

            helper = new LabelLogFilterHelper();
            helperDic[name] = helper;

            string scriptPath = GetScriptPath();
            string fileName = null;
            if (name == nameof(LabelLogFilterHelper))
            {
                fileName = "LabelLogFilterData.asset";
            }
            else
            {
                fileName = $"LabelLogFilterData-{name}.asset";
            }
            string dataPath = Path.Combine(Path.GetDirectoryName(scriptPath), fileName);

            LabelLogFilterData data = AssetDatabase.LoadAssetAtPath<LabelLogFilterData>(dataPath);

            if (data == null)
            {
                data = ScriptableObject.CreateInstance<LabelLogFilterData>();

                string directory = Path.GetDirectoryName(dataPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AssetDatabase.CreateAsset(data, dataPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                UnityEngine.Debug.Log($"已创建LabelLogFilterData文件: {dataPath}");
            }

            helper.filterData = data;
            if (helper.CheckDebugLabel(new LabelLog { Label = nameof(LabelLogFilterHelper) }))
            {
                UnityEngine.Debug.Log($"<{nameof(LabelLogFilterHelper)}> 已创建名为 {name} 的 {nameof(LabelLogFilterHelper)} 对象，使用的SO文件路径: {dataPath}");
            }
            return helper;
        }

        private static string GetScriptPath([CallerFilePath] string path = null)
        {
            string projectPath = Path.GetFullPath(Application.dataPath);
            projectPath = projectPath.Replace(Path.DirectorySeparatorChar, '/');
            path = path.Replace(Path.DirectorySeparatorChar, '/');

            if (path.StartsWith(projectPath))
            {
                return "Assets" + path.Substring(projectPath.Length);
            }
            return path;
        }

        private bool CheckDebugLabel(LabelLog log)
        {
            if (filterData == null)
                return true;

            if (filterData.debugDictionary.TryGetValue(log.Label, out var isEnabled))
            {
                return isEnabled;
            }
            else
            {
                filterData.debugDictionary.Add(log.Label, true);
                return true;
            }
        }
#endif
    }
}

