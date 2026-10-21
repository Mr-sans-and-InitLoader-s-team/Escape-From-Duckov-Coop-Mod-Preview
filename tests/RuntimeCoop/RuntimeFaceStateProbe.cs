using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CoopRuntimeTests;

internal static class RuntimeFaceStateProbe
{
    internal static object Run(Assembly mod)
    {
        var service = AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.NetService"), "Instance").GetValue(null);
        var server = (bool)AccessTools.Property(service.GetType(), "IsServer").GetValue(service);
        var remotes = (IDictionary)AccessTools.Field(service.GetType(), server ? "remoteCharacters" : "clientRemoteCharacters").GetValue(service);
        var local = AccessTools.Field(service.GetType(), "localPlayerStatus").GetValue(service);
        object Face(CustomFaceInstance face) => !face ? null : new
        {
            json = face.ConvertToSaveData().DataToJson(),
            model = face.GetInstanceID(),
            parts = face.GetComponentsInChildren<CustomFacePart>(true).Select(p => p.GetInstanceID()).OrderBy(i => i).ToArray()
        };
        return new
        {
            slot = Saves.SavesSystem.CurrentSlot,
            path = Saves.SavesSystem.CurrentFilePath,
            id = local == null ? null : AccessTools.Property(local.GetType(), "EndPoint").GetValue(local),
            own = Face(CharacterMainControl.Main?.characterModel.CustomFace),
            saved = LevelManager.Instance?.CustomFaceManager.LoadMainCharacterSetting().DataToJson(),
            wire = local == null ? null : AccessTools.Property(local.GetType(), "CustomFaceJson").GetValue(local),
            remotes = remotes.Keys.Cast<object>().Where(key => remotes[key] is GameObject go && go).Select(key => new
            {
                id = server ? (string)AccessTools.Method(service.GetType(), "GetPlayerId").Invoke(service, new[] { key }) : key.ToString(),
                face = Face(((GameObject)remotes[key]).GetComponent<CharacterMainControl>()?.characterModel.CustomFace)
            }).ToArray()
        };
    }
}
