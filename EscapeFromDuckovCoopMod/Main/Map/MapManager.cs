using Duckov.Scenes;
using Duckov.Utilities;
using Eflatun.SceneReference;
using System.Reflection;

namespace EscapeFromDuckovCoopMod.Main.Map
{

    public struct MapInfo
    {
        public string Name;
        public SceneReference Reference;
    }

    public class MapManager
    {

        // 反射修改基地场景数据
        private static readonly FieldInfo FI_baseScene =
            typeof(GameplayDataSettings.SceneManagementData).GetField("baseScene", BindingFlags.NonPublic | BindingFlags.Instance);

        // 备份修改之前的场景数据
        private List<SceneInfoEntry> entries;
        private SceneReference baseMap;

        // 创建实例
        private static MapManager instance = null;

        public static MapManager Instance
        {
            get
            {
                if (instance == null)
                {
                    SetInstance();
                }
                return instance;
            }
        }

        private static void SetInstance()
        {
            if (instance == null)
            {
                instance = new MapManager();
            }
        }

        MapManager() {
            entries = SceneInfoCollection.Entries;
            baseMap = GameplayDataSettings.SceneManagement.BaseScene;
        }

        // 获取地图列表
        public List<MapInfo> GetMapList() {
            return entries
                .Where(s => s.ID.EndsWith("_Main"))
                .Select(s => new MapInfo { Name = s.DisplayName, Reference = s.SceneReference })
                .ToList();
        }

        // 设置地图
        public bool SetDefaultMap(SceneReference Reference)
        {
            if (FI_baseScene != null && Reference != null)
            {
                FI_baseScene.SetValue(GameplayDataSettings.SceneManagement, Reference);
                return true;
            }
            return false;
        }

        // 还原地图
        public bool ResetBaseMap()
        {
            return SetDefaultMap(baseMap);
        }

    }
}
