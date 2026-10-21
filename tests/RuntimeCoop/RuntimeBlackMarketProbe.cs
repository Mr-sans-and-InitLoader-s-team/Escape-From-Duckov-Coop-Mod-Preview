using Duckov.BlackMarkets;
using Duckov.BlackMarkets.UI;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeBlackMarketProbe
{
    private static string Path(Transform t)=>t.parent?Path(t.parent)+"/"+t.name:t.name;
    internal static object Run(JObject command)
    {
        var market=BlackMarket.Instance;
        if(command["instance"]!=null)
            market=Object.FindObjectsByType<BlackMarket>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Single(m=>m.GetInstanceID()==(int)command["instance"]);
        if((bool?)command["open"]==true)BlackMarketView.Show(BlackMarketView.Mode.Supply);
        if((bool?)command["close"]==true)BlackMarketView.Instance?.Close();
        if(command["chance"]!=null)market.RefreshChance=(int)command["chance"];
        if(command["elapsed"]!=null)
            AccessTools.Field(typeof(BlackMarket),"lastRefreshedTimeRaw").SetValue(market,DateTime.UtcNow.AddSeconds(-(double)command["elapsed"]).ToBinary());
        if((bool?)command["refresh"]==true)market.PayAndRegenerate();
        if(command["call"]!=null)AccessTools.Method(typeof(BlackMarket),(string)command["call"]).Invoke(market,null);
        var view=BlackMarketView.Instance;
        return new {utc=DateTime.UtcNow,scale=Time.timeScale,
            singleton=BlackMarket.Instance?BlackMarket.Instance.GetInstanceID():0,
            view=view?new {view.open,target=view.Target?view.Target.GetInstanceID():0,
                chance=((TMPro.TMP_Text)AccessTools.Field(typeof(BlackMarketView),"refreshChanceText").GetValue(view)).text,
                eta=((TMPro.TMP_Text)AccessTools.Field(typeof(BlackMarketView),"refreshETAText").GetValue(view)).text}:null,
            markets=Object.FindObjectsByType<BlackMarket>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(m=>new {
                instance=m.GetInstanceID(),path=Path(m.transform),scene=m.gameObject.scene.name,m.enabled,active=m.gameObject.activeInHierarchy,
                chance=m.RefreshChance,max=m.MaxRefreshChance,remaining=m.RemainingTimeBeforeRefresh.TotalSeconds,
                interval=((TimeSpan)AccessTools.Property(typeof(BlackMarket),"TimeToRefresh").GetValue(m)).TotalSeconds,
                last=AccessTools.Field(typeof(BlackMarket),"lastRefreshedTimeRaw").GetValue(m),
                supplies=m.Supplies.Select(e=>new {id=e.ItemID,count=e.Remaining,price=e.TotalPrice}).ToArray(),
                demands=m.Demands.Select(e=>new {id=e.ItemID,count=e.Remaining,price=e.TotalPrice}).ToArray()}).ToArray()};
    }
}
