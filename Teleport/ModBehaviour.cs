// Decompiled with JetBrains decompiler
// Type: MapTeleport.ModBehaviour
// Assembly: MapTeleport, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: FF068137-406B-46D2-8ECE-F2F03FE1BFFB
// Assembly location: C:\SteamLibrary\steamapps\workshop\content\3167020\3591817603\MapTeleport.dll

using Cysharp.Threading.Tasks;
using Duckov.MiniMaps.UI;
using Duckov.Modding;
using Duckov.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

#nullable enable
namespace MapTeleport;

public class ModBehaviour : Duckov.Modding.ModBehaviour
{
  private const string TeleportHotkeyName = "teleport";
  private const KeyCode DefaultTeleportHotkey = (KeyCode) 116;
  private KeyCode teleportHotkey = (KeyCode) 116;

  private void OnEnable()
  {
    ModManager.OnScan += new Action<List<ModInfo>>(this.OnModScan);
    this.GetCustomHotkey();
  }

  private void OnDisable()
  {
    ModManager.OnScan -= new Action<List<ModInfo>>(this.OnModScan);
    CustomHotkeyHelper.RemoveEvent2OnCustomHotkeyChangedEvent(new Action(this.GetCustomHotkey));
    CustomHotkeyHelper.RemoveHotkey("teleport");
  }

  private void OnModScan(List<ModInfo> _) => this.GetCustomHotkey();

  private void GetCustomHotkey()
  {
    CustomHotkeyHelper.TryInit();
    KeyCode hotkey = CustomHotkeyHelper.GetHotkey("teleport");
    this.teleportHotkey = hotkey == KeyCode.None ? (KeyCode) 116 : hotkey;
    CustomHotkeyHelper.AddNewHotkey("teleport", (KeyCode) 116, "地图传送");
    CustomHotkeyHelper.TryAddEvent2OnCustomHotkeyChangedEvent(new Action(this.GetCustomHotkey));
  }

  private void Update()
  {
    if (!Input.GetKeyDown(this.teleportHotkey) || !this.IsMapOpen())
      return;
    this.Teleport();
  }

  private async void Teleport()
  {
    MiniMapView miniMapView = MiniMapView.Instance;
    Type miniMapViewType = miniMapView.GetType();
    FieldInfo miniMapDisplayField = miniMapViewType.GetField("display", BindingFlags.Instance | BindingFlags.NonPublic);
    MiniMapDisplay miniMapDisplay;
    CharacterMainControl mainCharacter;
    if (miniMapDisplayField == (FieldInfo) null)
    {
      miniMapView = (MiniMapView) null;
      miniMapViewType = (Type) null;
      miniMapDisplayField = (FieldInfo) null;
      miniMapDisplay = (MiniMapDisplay) null;
      mainCharacter = (CharacterMainControl) null;
    }
    else
    {
      miniMapDisplay = miniMapDisplayField.GetValue((object) miniMapView) as MiniMapDisplay;
      if (miniMapDisplay == null)
      {
        miniMapView = (MiniMapView) null;
        miniMapViewType = (Type) null;
        miniMapDisplayField = (FieldInfo) null;
        miniMapDisplay = (MiniMapDisplay) null;
        mainCharacter = (CharacterMainControl) null;
      }
      else
      {
        Vector3 targetPos;
        if (!miniMapDisplay.TryConvertToWorldPosition(CharacterInputControl.Instance.inputManager.MousePos, out targetPos))
        {
          miniMapView = (MiniMapView) null;
          miniMapViewType = (Type) null;
          miniMapDisplayField = (FieldInfo) null;
          miniMapDisplay = (MiniMapDisplay) null;
          mainCharacter = (CharacterMainControl) null;
        }
        else
        {
          mainCharacter = LevelManager.Instance.MainCharacter;
          if (mainCharacter == null)
          {
            miniMapView = (MiniMapView) null;
            miniMapViewType = (Type) null;
            miniMapDisplayField = (FieldInfo) null;
            miniMapDisplay = (MiniMapDisplay) null;
            mainCharacter = (CharacterMainControl) null;
          }
          else
          {
            ((ManagedUIElement) miniMapView).Close();
            UniTask uniTask = BlackScreen.ShowAndReturnTask((AnimationCurve) null, 0.0f, 0.2f);
            await uniTask;
            this.FixZoneTriggerExit(mainCharacter);
            Vector3 fitPos;
            if (this.TryGetFitPosition(targetPos, out fitPos))
              mainCharacter.SetPosition(fitPos);
            else
              mainCharacter.PopText("未找到落脚点", -1f);
            uniTask = BlackScreen.HideAndReturnTask((AnimationCurve) null, 0.0f, 0.5f);
            await uniTask;
            miniMapView = (MiniMapView) null;
            miniMapViewType = (Type) null;
            miniMapDisplayField = (FieldInfo) null;
            miniMapDisplay = (MiniMapDisplay) null;
            mainCharacter = (CharacterMainControl) null;
          }
        }
      }
    }
  }

  private void FixZoneTriggerExit(CharacterMainControl mainCharacter)
  {
    Scene activeScene = SceneManager.GetActiveScene();
    GameObject[] rootGameObjects = activeScene.GetRootGameObjects();
    if (rootGameObjects == null || rootGameObjects.Length == 0)
      return;
    foreach (GameObject gameObject in rootGameObjects)
    {
      Zone[] componentsInChildren = gameObject.GetComponentsInChildren<Zone>();
      if (componentsInChildren != null && componentsInChildren.Length != 0)
      {
        foreach (Zone zone in componentsInChildren)
        {
          HashSet<Health> healths = zone.Healths;
          if (healths != null && healths.Count != 0)
            zone.Healths.Remove(mainCharacter.Health);
        }
      }
    }
  }

  private bool TryGetFitPosition(Vector3 targetPos, out Vector3 currentPos)
  {
    currentPos = Vector3.zero;
    RaycastHit raycastHit;
    Physics.Raycast(new Vector3(targetPos.x, 1000f, targetPos.z), Vector3.down, out raycastHit, float.PositiveInfinity);
    if (raycastHit.collider == null)
      return false;
    currentPos = new Vector3(targetPos.x, raycastHit.point.y + 0.5f, targetPos.z);
    return true;
  }

  private bool IsMapOpen()
  {
    MiniMapView instance = MiniMapView.Instance;
    return instance != null && View.ActiveView == instance;
  }
}
