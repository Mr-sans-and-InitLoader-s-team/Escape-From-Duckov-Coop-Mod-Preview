using System.Reflection;
using HarmonyLib;
using NodeCanvas.Framework;
using NodeCanvas.Tasks.Actions;
using SodaCraft.Localizations;

namespace EscapeFromDuckovCoopMod;

//[HarmonyPatch(typeof(CharacterMainControl))]
//internal static class AIPopTextPatch
//{
//    [HarmonyPatch("PopText", typeof(string), typeof(float))]
//    [HarmonyPostfix]
//    private static void BroadcastAIPopText(CharacterMainControl __instance, string text, float speed)
//    {
//        if (!NetService.Instance.IsServer)
//            return;

//        AIAwarenessSync.TrySendPopText(__instance, text, speed);
//    }
//}

[HarmonyPatch(typeof(AIMainBrain))]
internal static class AISoundEventPatch
{
    [HarmonyPatch("MakeSound", typeof(AISound))]
    [HarmonyPostfix]
    private static void BroadcastAISound(AISound sound)
    {
        AIAwarenessSync.TrySendSound(sound);
    }
}

[HarmonyPatch(typeof(PostSound))]
internal static class AIVoiceSoundPatch
{
    private static readonly PropertyInfo AgentProperty = typeof(ActionTask<AICharacterController>)
        .GetProperty("agent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

    [HarmonyPatch("OnExecute")]
    [HarmonyPostfix]
    private static void BroadcastAIVoice(PostSound __instance)
    {
        if (__instance == null)
            return;

        var agent = AgentProperty?.GetValue(__instance) as AICharacterController;
        var cmc = agent ? agent.CharacterMainControl : null;
        if (agent == null || cmc == null || !NetService.Instance.IsServer)
            return;

        AIAwarenessSync.TrySendVoice(agent, cmc, __instance.voiceSound);
    }
}

[HarmonyPatch(typeof(PopText))]
internal static class AIPopTextPatch_1
{
    private static readonly PropertyInfo AgentProperty = typeof(ActionTask<AICharacterController>)
        .GetProperty("agent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

    [HarmonyPatch("OnExecute")]
    [HarmonyPostfix]
    private static void BroadcastAIVoice(PopText __instance)
    {
        if (__instance == null)
            return;

        var agent = AgentProperty?.GetValue(__instance) as AICharacterController;
        var cmc = agent ? agent.CharacterMainControl : null;
        if (agent == null || cmc == null || !NetService.Instance.IsServer)
            return;

        AIAwarenessSync.TrySendPopText(agent, __instance.content.value.ToPlainText(), -1f);
    }
}