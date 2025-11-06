using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LiteNetLib.Utils;
using LiteNetLib;
using Steamworks;

namespace EscapeFromDuckovCoopMod;

public class KillFeedManager : MonoBehaviour
{
    public static KillFeedManager Instance { get; private set; }

    private class KillRecord
    {
        public GameObject container;
        public TextMeshProUGUI killerText;
        public TextMeshProUGUI victimText;
        public Image weaponIcon;
        public CanvasGroup canvasGroup;
        public float creationTime;
        public string killer;
        public string victim;
    }

    private RectTransform killFeedContainer;
    private List<KillRecord> activeRecords = new List<KillRecord>();
    private Queue<KillRecord> killFeedQueue = new Queue<KillRecord>();

    // 配置
    private const float FontSize = 24f;
    private const int MaxRecords = 6;
    private const float FadeInTime = 0.3f;
    private const float FadeOutTime = 0.5f;
    private const float DisplayTime = 5f;
    private const float SlideInTime = 0.3f;
    private const float RecordHeight = 40f;
    private const float RecordSpacing = 5f;
    private const float RightMargin = 80f;
    private const float TopMargin = 150f;
    private const float WeaponIconSize = 32f;

    private bool _rpcRegistered = false;

    public void Init()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        RegisterRPCs();
    }

    private void RegisterRPCs()
    {
        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
        {
            Debug.LogWarning("[KillFeed] HybridRPCManager not found, RPC mode disabled");
            return;
        }

        rpcManager.RegisterRPC("KillFeedEvent", OnRPC_KillFeedEvent);
        _rpcRegistered = true;
        Debug.Log("[KillFeed] RPCs registered successfully");
    }

    private bool _eventSubscribed = false;

    private void Update()
    {
        // 延迟订阅死亡事件，等待主角加载
        if (!_eventSubscribed)
        {
            var mainChar = LevelManager.Instance?.MainCharacter;
            if (mainChar != null && mainChar.Health != null)
            {
                try
                {
                    Health.OnDead += OnAnyCharacterDead;
                    _eventSubscribed = true;
                    Debug.Log("[KillFeed] Subscribed to OnDead event");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[KillFeed] Failed to subscribe to OnDead: {e.Message}");
                }
            }
        }
    }

    private void OnAnyCharacterDead(Health health, DamageInfo dmgInfo)
    {
        // 只处理本地玩家死亡
        if (health == null || health.TryGetCharacter() == null) return;
        
        var victim = health.TryGetCharacter();
        if (victim == null || !victim.IsMainCharacter) return;

        OnLocalPlayerDead(health, dmgInfo);
    }

    private void EnsureKillFeedUI()
    {
        if (killFeedContainer != null) return;

        var hudManager = FindObjectOfType<HUDManager>();
        if (hudManager == null)
        {
            return;
        }

        var containerGO = new GameObject("KillFeedContainer");
        killFeedContainer = containerGO.AddComponent<RectTransform>();
        killFeedContainer.SetParent(hudManager.transform, false);

        killFeedContainer.anchorMin = new Vector2(1f, 1f);
        killFeedContainer.anchorMax = new Vector2(1f, 1f);
        killFeedContainer.pivot = new Vector2(1f, 1f);
        killFeedContainer.anchoredPosition = new Vector2(-RightMargin, -TopMargin);
        killFeedContainer.sizeDelta = new Vector2(400f, 500f);

        Debug.Log("[KillFeed] UI created successfully");
    }

    private void OnLocalPlayerDead(Health health, DamageInfo dmgInfo)
    {
        if (dmgInfo.fromCharacter == null) return;

        var killer = dmgInfo.fromCharacter;
        var victim = LevelManager.Instance?.MainCharacter;
        if (victim == null) return;

        string killerName = GetCharacterDisplayName(killer, true);
        string victimName = GetMyDisplayName();
        int weaponIdInt = dmgInfo.fromWeaponItemID;

        // 本地显示
        AddKillRecordLocal(killerName, victimName, weaponIdInt, isLocalVictim: true);

        // 广播给其他玩家
        BroadcastKillEvent(killerName, victimName, weaponIdInt);
    }

    private string GetMyDisplayName()
    {
        var service = NetService.Instance;
        if (service == null || service.localPlayerStatus == null)
        {
            return "玩家";
        }

        if (SteamManager.Initialized && service.TransportMode == NetworkTransportMode.SteamP2P)
        {
            return SteamFriends.GetPersonaName();
        }
        else
        {
            // 直连模式，显示IP
            return service.localPlayerStatus.EndPoint ?? "玩家";
        }
    }

    private string GetCharacterDisplayName(CharacterMainControl character, bool isKiller)
    {
        if (character == null) return "未知";

        // 如果是玩家自己
        if (character.IsMainCharacter)
        {
            return GetMyDisplayName();
        }

        // 如果是远程玩家，尝试从网络信息获取
        var service = NetService.Instance;
        if (service != null && service.IsServer)
        {
            // 服务器端：从playerStatuses查找
            foreach (var kv in service.playerStatuses)
            {
                if (service.remoteCharacters.TryGetValue(kv.Key, out var go) && go == character.gameObject)
                {
                    if (SteamManager.Initialized && service.TransportMode == NetworkTransportMode.SteamP2P)
                    {
                        var ep = kv.Key.EndPoint as System.Net.IPEndPoint;
                        if (ep != null && VirtualEndpointManager.Instance != null &&
                            VirtualEndpointManager.Instance.TryGetSteamID(ep, out var steamId))
                        {
                            return SteamFriends.GetFriendPersonaName(steamId);
                        }
                    }
                    return kv.Value.EndPoint ?? "远程玩家";
                }
            }
        }
        else if (service != null && !service.IsServer)
        {
            // 客户端：从clientRemoteCharacters查找
            foreach (var kv in service.clientRemoteCharacters)
            {
                if (kv.Value == character.gameObject)
                {
                    if (SteamManager.Initialized && service.TransportMode == NetworkTransportMode.SteamP2P)
                    {
                        // 尝试解析Steam名称
                        if (kv.Key.StartsWith("steam_") && ulong.TryParse(kv.Key.Substring(6), out var steamIdVal))
                        {
                            var steamId = new CSteamID(steamIdVal);
                            return SteamFriends.GetFriendPersonaName(steamId);
                        }
                    }
                    return kv.Key;
                }
            }
        }

        // 如果是NPC
        if (character.characterPreset != null)
        {
            return character.characterPreset.DisplayName;
        }

        return "未知";
    }

    private void BroadcastKillEvent(string killer, string victim, int weaponId)
    {
        var service = NetService.Instance;
        if (service == null || !service.networkStarted) return;

        if (_rpcRegistered)
        {
            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager != null)
            {
                var target = service.IsServer ? Net.HybridP2P.RPCTarget.AllClients : Net.HybridP2P.RPCTarget.Server;
                
                rpcManager.CallRPC("KillFeedEvent", target, 0, writer =>
                {
                    writer.Put(killer);
                    writer.Put(victim);
                    writer.Put(weaponId);
                }, DeliveryMethod.ReliableOrdered);

                Debug.Log($"[KillFeed] Broadcast kill event via RPC: {killer} -> {victim}");
            }
        }
    }

    private void OnRPC_KillFeedEvent(long senderConnectionId, NetDataReader reader)
    {
        string killer = reader.GetString();
        string victim = reader.GetString();
        int weaponId = reader.GetInt();

        Debug.Log($"[KillFeed] Received kill event via RPC: {killer} -> {victim}");

        // 显示击杀记录
        AddKillRecordLocal(killer, victim, weaponId, isLocalVictim: false);

        // 如果是服务器，继续转发给其他客户端
        var service = NetService.Instance;
        if (service != null && service.IsServer && _rpcRegistered)
        {
            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager != null)
            {
                rpcManager.CallRPC("KillFeedEvent", Net.HybridP2P.RPCTarget.AllClients, 0, writer =>
                {
                    writer.Put(killer);
                    writer.Put(victim);
                    writer.Put(weaponId);
                }, DeliveryMethod.ReliableOrdered);
            }
        }
    }

    private void AddKillRecordLocal(string killer, string victim, int weaponId, bool isLocalVictim)
    {
        EnsureKillFeedUI();
        if (killFeedContainer == null) return;

        var record = CreateKillRecordUI(killer, victim, weaponId, isLocalVictim);
        killFeedQueue.Enqueue(record);

        ProcessKillFeedQueue();
    }

    private KillRecord CreateKillRecordUI(string killer, string victim, int weaponId, bool isLocalVictim)
    {
        var recordGO = new GameObject("KillRecord");
        var rectTransform = recordGO.AddComponent<RectTransform>();
        rectTransform.SetParent(killFeedContainer, false);
        rectTransform.sizeDelta = new Vector2(400f, RecordHeight);

        var horizontalLayout = recordGO.AddComponent<HorizontalLayoutGroup>();
        horizontalLayout.spacing = 5f;
        horizontalLayout.childAlignment = TextAnchor.MiddleRight;
        horizontalLayout.childControlWidth = false;
        horizontalLayout.childControlHeight = false;
        horizontalLayout.childForceExpandWidth = false;
        horizontalLayout.childForceExpandHeight = false;

        var canvasGroup = recordGO.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        // 击杀者名称
        var killerGO = new GameObject("Killer");
        var killerText = killerGO.AddComponent<TextMeshProUGUI>();
        killerText.text = killer;
        killerText.fontSize = FontSize;
        killerText.color = Color.white;
        killerText.alignment = TextAlignmentOptions.Right;
        killerText.raycastTarget = false;
        killerGO.transform.SetParent(recordGO.transform, false);
        var killerRect = killerGO.GetComponent<RectTransform>();
        killerRect.sizeDelta = new Vector2(150f, RecordHeight);

        // 武器图标
        var weaponGO = new GameObject("Weapon");
        var weaponImage = weaponGO.AddComponent<Image>();
        weaponImage.sprite = GetWeaponSprite(weaponId);
        weaponImage.raycastTarget = false;
        weaponGO.transform.SetParent(recordGO.transform, false);
        var weaponRect = weaponGO.GetComponent<RectTransform>();
        weaponRect.sizeDelta = new Vector2(WeaponIconSize, WeaponIconSize);

        // 受害者名称
        var victimGO = new GameObject("Victim");
        var victimText = victimGO.AddComponent<TextMeshProUGUI>();
        victimText.text = victim;
        victimText.fontSize = FontSize;
        victimText.color = isLocalVictim ? new Color(1f, 0.3f, 0.3f) : Color.gray;
        victimText.alignment = TextAlignmentOptions.Left;
        victimText.raycastTarget = false;
        victimGO.transform.SetParent(recordGO.transform, false);
        var victimRect = victimGO.GetComponent<RectTransform>();
        victimRect.sizeDelta = new Vector2(150f, RecordHeight);

        var record = new KillRecord
        {
            container = recordGO,
            killerText = killerText,
            victimText = victimText,
            weaponIcon = weaponImage,
            canvasGroup = canvasGroup,
            creationTime = Time.time,
            killer = killer,
            victim = victim
        };

        return record;
    }

    private Sprite _cachedWeaponSprite = null;
    private int _lastWeaponId = -1;

    private Sprite GetWeaponSprite(int weaponId)
    {
        if (weaponId <= 0)
        {
            return null;
        }

        if (weaponId == _lastWeaponId && _cachedWeaponSprite != null)
        {
            return _cachedWeaponSprite;
        }

        try
        {
            StartCoroutine(LoadWeaponSpriteAsync(weaponId));
            return _cachedWeaponSprite;
        }
        catch
        {
            return null;
        }
    }

    private IEnumerator LoadWeaponSpriteAsync(int weaponId)
    {
        var task = COOPManager.GetItemAsync(weaponId);
        while (!task.GetAwaiter().IsCompleted)
        {
            yield return null;
        }

        var item = task.GetAwaiter().GetResult();
        if (item != null && item.Icon != null)
        {
            _lastWeaponId = weaponId;
            _cachedWeaponSprite = item.Icon;
        }
    }

    private void ProcessKillFeedQueue()
    {
        while (killFeedQueue.Count > 0 && activeRecords.Count < MaxRecords)
        {
            var record = killFeedQueue.Dequeue();
            activeRecords.Add(record);
            StartCoroutine(AnimateRecordIn(record));
        }

        UpdateRecordsPosition();
    }

    private IEnumerator AnimateRecordIn(KillRecord record)
    {
        float elapsed = 0f;
        var startPos = record.container.GetComponent<RectTransform>().anchoredPosition;
        startPos.x = 400f; // 从右侧滑入

        while (elapsed < SlideInTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / SlideInTime;
            
            record.canvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            var rect = record.container.GetComponent<RectTransform>();
            rect.anchoredPosition = Vector2.Lerp(startPos, Vector2.zero, t);

            yield return null;
        }

        record.canvasGroup.alpha = 1f;

        // 等待显示时间后淡出
        yield return new WaitForSeconds(DisplayTime);

        // 淡出
        elapsed = 0f;
        while (elapsed < FadeOutTime)
        {
            elapsed += Time.deltaTime;
            record.canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / FadeOutTime);
            yield return null;
        }

        // 移除记录
        activeRecords.Remove(record);
        if (record.container != null)
        {
            Destroy(record.container);
        }

        UpdateRecordsPosition();
        ProcessKillFeedQueue();
    }

    private void UpdateRecordsPosition()
    {
        for (int i = 0; i < activeRecords.Count; i++)
        {
            var record = activeRecords[i];
            var rectTransform = record.container.GetComponent<RectTransform>();
            
            float targetY = -i * (RecordHeight + RecordSpacing);
            rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, targetY);
        }
    }

    private void OnDestroy()
    {
        foreach (var record in activeRecords)
        {
            if (record.container != null)
                Destroy(record.container);
        }
        if (killFeedContainer != null)
            Destroy(killFeedContainer.gameObject);
    }
}

