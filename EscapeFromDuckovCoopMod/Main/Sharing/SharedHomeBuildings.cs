using Duckov.Buildings;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeBuildings
{
    internal static void Init() => BuildingManager.OnBuildingListChanged += SharedHome.MarkDirty;
    internal static bool ValidPlacement(string areaId, string buildingId, int x, int y, int rotation)
    {
        if (rotation < 0 || rotation > 3 || !BuildingManager.Instance) return false;
        var info = BuildingManager.GetBuildingInfo(buildingId);
        if (!info.Valid) return false;
        if (!(bool)AccessTools.Method(typeof(BuildingInfo), "RequirementsSatisfied").Invoke(info, null)) return false;
        var area = UnityEngine.Object.FindObjectsByType<BuildingArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(a => a.AreaID == areaId);
        var point = new Vector2Int(x, y);
        return area && area.IsPlacementWithinRange(info.Dimensions, (BuildingRotation)rotation, point) &&
            !area.AreaData.Collide(info.Dimensions, (BuildingRotation)rotation, point);
    }
    internal static void ApplyLayout()
    {
        if (!BuildingManager.Instance) return;
        SharedHomeSave.Load(BuildingManager.Instance);
        foreach (var area in Object.FindObjectsByType<BuildingArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!area.gameObject.scene.IsValid()) continue;
            var active = (List<Building>)AccessTools.Field(typeof(BuildingArea), "activeBuildings").GetValue(area);
            var desired = area.AreaData.Buildings.ToDictionary(b => b.GUID);
            foreach (var building in active.ToArray())
            {
                if (!building) { active.Remove(building); continue; }
                if (desired.TryGetValue(building.GUID, out var data))
                {
                    building.transform.position = (Vector3)AccessTools.Method(typeof(BuildingManager.BuildingData), "GetTransformPosition").Invoke(data, null);
                    building.transform.localRotation = Quaternion.Euler(0, (int)data.Rotation * 90, 0);
                    desired.Remove(building.GUID);
                    continue;
                }
                building.gameObject.SetActive(false);
                Object.Destroy(building.gameObject);
                active.Remove(building);
            }
            foreach (var data in desired.Values)
                AccessTools.Method(typeof(BuildingArea), "Display").Invoke(area, new object[] { data });
        }
    }
}
