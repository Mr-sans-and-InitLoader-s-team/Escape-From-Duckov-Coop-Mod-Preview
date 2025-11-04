using Duckov.Modding;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MapTeleport;

public static class CustomHotkeyHelper
{
  private const string ModName = "MapTeleport";
  private static Duckov.Modding.ModBehaviour customHotkey;
  private static MethodInfo addNewHotkeyMethod;
  private static MethodInfo removeHotkeyMethod;
  private static MethodInfo getHotkeyMethod;
  private static EventInfo onCustomHotkeyChangedEvent;

  public static void TryInit()
  {
    if (customHotkey != null)
      return;
    var customHotkeyModInfo = TryGetCustomHotkeyModInfo();
    if (!customHotkeyModInfo.isFind || !ModManager.IsModActive(customHotkeyModInfo.modInfo, out customHotkey))
      return;
    Type type = customHotkey.GetType();
    addNewHotkeyMethod = type.GetMethod("AddNewHotkey", BindingFlags.Instance | BindingFlags.Public);
    removeHotkeyMethod = type.GetMethod("RemoveHotkey", BindingFlags.Instance | BindingFlags.Public);
    getHotkeyMethod = type.GetMethod("GetHotkey", BindingFlags.Instance | BindingFlags.Public);
    onCustomHotkeyChangedEvent = type.GetEvent("OnCustomHotkeyChanged", BindingFlags.Public | BindingFlags.Static);
  }

  public static void AddNewHotkey(string saveName, KeyCode defaultHotkey, string showName)
  {
    if (customHotkey == null)
      return;
    addNewHotkeyMethod?.Invoke(customHotkey, new object[4]
    {
      "MapTeleport",
      saveName,
      defaultHotkey,
      showName
    });
  }

  public static void RemoveHotkey(string saveName)
  {
    if (customHotkey == null)
      return;
    removeHotkeyMethod?.Invoke(customHotkey, new object[2]
    {
      "MapTeleport",
      saveName
    });
  }

  public static KeyCode GetHotkey(string saveName)
  {
    if (customHotkey == null)
      return KeyCode.None;
    
    object result = getHotkeyMethod?.Invoke(customHotkey, new object[2]
    {
      "MapTeleport",
      saveName
    });
    
    if (result == null)
      return KeyCode.None;
      
    if (Enum.TryParse<KeyCode>(result.ToString(), out KeyCode keyCode))
      return keyCode;
      
    return KeyCode.None;
  }

  public static void TryAddEvent2OnCustomHotkeyChangedEvent(Action callback)
  {
    if (onCustomHotkeyChangedEvent == null)
      return;
    onCustomHotkeyChangedEvent.RemoveEventHandler(null, callback);
    onCustomHotkeyChangedEvent.AddEventHandler(null, callback);
  }

  public static void RemoveEvent2OnCustomHotkeyChangedEvent(Action callback)
  {
    onCustomHotkeyChangedEvent?.RemoveEventHandler(null, callback);
  }

  private static (bool isFind, ModInfo modInfo) TryGetCustomHotkeyModInfo()
  {
    List<ModInfo> modInfos = ModManager.modInfos;
    if (modInfos == null || modInfos.Count == 0)
      return (false, new ModInfo());
    foreach (ModInfo modInfo in modInfos)
    {
      if (modInfo.publishedFileId == 3594709838UL)
        return (true, modInfo);
    }
    return (false, new ModInfo());
  }
}
