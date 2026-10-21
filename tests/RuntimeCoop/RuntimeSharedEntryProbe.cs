using System.Reflection;
using Duckov.Utilities;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CoopRuntimeTests;

internal static class RuntimeSharedEntryProbe
{
    internal static object Run(Assembly mod, JObject command)
    {
        RuntimeSharedHomeProbe.PrepareIdentity(mod);
        var home = mod.GetType("EscapeFromDuckovCoopMod.SharedHome");
        var uiType = mod.GetType("EscapeFromDuckovCoopMod.MModUI");
        var ui = AccessTools.Field(uiType,"Instance").GetValue(null);
        if (command["enabled"] != null) home.GetMethod("SetEnabled").Invoke(null,new object[]{(bool)command["enabled"]});
        if ((bool?)command["menu"] == true) SceneLoader.LoadMainMenu();
        if (command["uiJoin"] != null)
        {
            var components = AccessTools.Field(uiType,"_components").GetValue(ui);
            ((TMPro.TMP_InputField)AccessTools.Field(components.GetType(),"IpInputField").GetValue(components)).text="127.0.0.1";
            ((TMPro.TMP_InputField)AccessTools.Field(components.GetType(),"PortInputField").GetValue(components)).text=((int)command["uiJoin"]).ToString();
            AccessTools.Method(uiType,"OnManualConnect").Invoke(ui,null);
        }
        return new
        {
            canConnect=AccessTools.Method(uiType,"CheckCanConnect").Invoke(ui,null),
            menu=AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeEntry"),"AtMainMenu").GetValue(null),
            active=home.GetProperty("Active").GetValue(null),
            overlay=AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeSave"),"OverlayActive").GetValue(null),
            ready=AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeLoadouts"),"Ready").GetValue(null),
            game=Duckov.GameMetaData.Instance.Version.ToString(),
            savedXp=Saves.SavesSystem.Load<long>("EXP_Value"),
            key=AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeCharacterContext"),"Read").Invoke(
                AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeLoadouts"),"SaveContext").GetValue(null),new object[]{LevelManager.Instance})
        };
    }
}
