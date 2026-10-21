using HarmonyLib;
using UnityEngine;

namespace CoopRuntimeTests;

internal static class RuntimeExits
{
    internal static CountDownArea[] Areas() => UnityEngine.Object.FindObjectsByType<CountDownArea>(FindObjectsInactive.Include,FindObjectsSortMode.None)
        .Where(a=>a && a.gameObject.scene.IsValid()).OrderBy(a=>a.GetInstanceID()).ToArray();

    internal static object Catalog() => Areas().Select(a=>new { id=a.GetInstanceID(),name=a.name,
        active=a.gameObject.activeInHierarchy,enabled=a.enabled,seconds=a.RequiredExtrationTime,
        position=new[]{a.transform.position.x,a.transform.position.y,a.transform.position.z},
        events=Enumerable.Range(0,a.onCountDownSucceed.GetPersistentEventCount()).Select(i=>new {
            target=a.onCountDownSucceed.GetPersistentTarget(i)?.GetType().FullName,
            method=a.onCountDownSucceed.GetPersistentMethodName(i) }).ToArray(),
        proxies=a.GetComponentsInChildren<SceneLoaderProxy>(true).Select(p=>new {name=p.name,
            target=(string)AccessTools.Field(typeof(SceneLoaderProxy),"sceneID").GetValue(p)}).ToArray() }).ToArray();

    internal static object Enter(int id)
    {
        var area=Areas().Single(a=>a.GetInstanceID()==id);
        var character=CharacterMainControl.Main;
        character.SetPosition(area.transform.position);
        var collider=character.GetComponent<Collider>();
        if(!collider)throw new InvalidOperationException("No native character collider");
        AccessTools.Method(typeof(CountDownArea),"OnTriggerEnter").Invoke(area,new object[]{collider});
        return new {id,area.RequiredExtrationTime,area.RemainingTime};
    }
}
