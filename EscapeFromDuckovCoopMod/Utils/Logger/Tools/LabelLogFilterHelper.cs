using EscapeFromDuckovCoopMod.Utils.Logger.LogFilters;
using EscapeFromDuckovCoopMod.Utils.Logger.Logs;
using System.Diagnostics;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EscapeFromDuckovCoopMod.Utils.Logger.Tools
{
    /// <summary>
    /// 用于管理标签日志过滤的辅助类
    /// </summary>
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

            // 获取当前脚本文件路径
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

            // 尝试加载现有的SO文件
            LabelLogFilterData data = AssetDatabase.LoadAssetAtPath<LabelLogFilterData>(dataPath);

            // 如果没有找到，则自动创建
            if (data == null)
            {
                // 创建SO对象
                data = ScriptableObject.CreateInstance<LabelLogFilterData>();

                // 确保目录存在
                string directory = Path.GetDirectoryName(dataPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 保存为SO文件
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
            // 将绝对路径转换为相对于Unity项目的路径 (例如 "Assets/...")
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
            // 如果 SO 引用为空，不进行任何处理
            if (filterData == null)
                return true;

            if (filterData.debugDictionary.TryGetValue(log.Label, out var isEnabled))
            {
                return isEnabled;
            }
            else
            {
                filterData.debugDictionary.Add(log.Label, true);
                // 在编辑器模式下保存数据
                //filterData.SaveData();
                return true;
            }
        }

        //public void UpdateFilterSetting(string label, bool isEnabled)
        //{
        //    if (filterData == null)
        //        return;

        //    filterData.debugDictionary[label] = isEnabled;


        //    if (!Application.isPlaying)
        //    {
        //        filterData.SaveData();
        //    }

        //}

        //public bool GetFilterSetting(string label)
        //{
        //    if (filterData == null)
        //        return true;

        //    return filterData.debugDictionary.TryGetValue(label, out var isEnabled) ? isEnabled : true;
        //}
#endif
    }
}