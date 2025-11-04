using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using Duckov.Utilities;
using Steamworks;

namespace EscapeFromDuckovCoopMod;

public class PlayerColorManager : MonoBehaviour
{
    public static PlayerColorManager Instance { get; private set; }

    private static readonly Color[] PlayerColors = new Color[]
    {
        Color.red,
        Color.blue,
        Color.yellow,
        Color.magenta,
        Color.cyan,
        new Color(1f, 0.5f, 0f),
        new Color(0.5f, 0f, 1f),
        new Color(0f, 1f, 0.5f),
        new Color(1f, 0f, 0.5f),
        new Color(0.5f, 1f, 0f)
    };

    private static readonly Color LocalPlayerColor = Color.green;

    private readonly Dictionary<string, Color> _playerColors = new Dictionary<string, Color>();
    private readonly HashSet<Color> _usedColors = new HashSet<Color>();
    private readonly Dictionary<Health, HealthBar> _healthBarCache = new Dictionary<Health, HealthBar>();
    private readonly System.Random _random = new System.Random();

    private static MethodInfo _getActiveHealthBarMethod;
    private static FieldInfo _fillField;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        Debug.Log("[PlayerColorManager] Player color system initialized with 10 predefined colors");
        Debug.Log("[PlayerColorManager] Local player color: Green");
    }
    
    private void Start()
    {
        InitializeReflection();
        StartCoroutine(UpdateColorsRoutine());
    }

    private void InitializeReflection()
    {
        try
        {
            var healthBarManagerType = typeof(HealthBarManager);
            _getActiveHealthBarMethod = AccessTools.DeclaredMethod(healthBarManagerType, "GetActiveHealthBar", new[] { typeof(Health) });
            
            if (_getActiveHealthBarMethod == null)
            {
                Debug.LogWarning("[PlayerColorManager] GetActiveHealthBar method not found");
            }

            var healthBarType = typeof(HealthBar);
            _fillField = AccessTools.Field(healthBarType, "fill");
            
            if (_fillField == null)
            {
                Debug.LogWarning("[PlayerColorManager] Fill field not found in HealthBar");
            }

            if (_getActiveHealthBarMethod != null && _fillField != null)
            {
                Debug.Log("[PlayerColorManager] Health bar reflection initialized successfully");
                Debug.Log("[PlayerColorManager] Will modify fill.color to change health bar color");
            }
            else
            {
                Debug.LogWarning("[PlayerColorManager] Failed to initialize health bar reflection");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[PlayerColorManager] Reflection initialization error: {ex.Message}");
        }
    }

    private IEnumerator UpdateColorsRoutine()
    {
        yield return new WaitForSeconds(3f);
        
        while (true)
        {
            yield return new WaitForSeconds(0.5f);
            
            if (Service == null || !Service.networkStarted)
                continue;

            ApplyExistingColors();
        }
    }

    private void AssignRandomColorsToPlayers()
    {
        if (!Service.networkStarted)
            return;
            
        if (IsServer && remoteCharacters != null)
        {
            foreach (var kvp in remoteCharacters)
            {
                if (Service.playerStatuses.TryGetValue(kvp.Key, out var st))
                {
                    string playerId = st.EndPoint;
                    string playerName = GetPlayerDisplayName(st);
                    GameObject playerObject = kvp.Value;

                    if (!_playerColors.ContainsKey(playerId))
                    {
                        Color randomColor = GetAvailableColor();
                        _playerColors[playerId] = randomColor;
                        _usedColors.Add(randomColor);
                        Debug.Log($"[PlayerColorManager] Assigned color to player {playerId}: {randomColor}");
                    }
                    
                    ApplyColorAndNameToPlayer(playerId, playerName, playerObject, _playerColors[playerId]);
                }
            }
        }
        else if (clientRemoteCharacters != null)
        {
            foreach (var kvp in clientRemoteCharacters)
            {
                string playerId = kvp.Key;
                GameObject playerObject = kvp.Value;
                
                string playerName = null;
                if (Service.clientPlayerStatuses != null && Service.clientPlayerStatuses.TryGetValue(playerId, out var st))
                {
                    playerName = GetPlayerDisplayName(st);
                }

                if (!_playerColors.ContainsKey(playerId))
                {
                    Color randomColor = GetAvailableColor();
                    _playerColors[playerId] = randomColor;
                    _usedColors.Add(randomColor);
                    Debug.Log($"[PlayerColorManager] Assigned color to player {playerId}: {randomColor}");
                }
                
                ApplyColorAndNameToPlayer(playerId, playerName, playerObject, _playerColors[playerId]);
            }
        }
    }
    
    private Color GetAvailableColor()
    {
        var availableColors = new List<Color>();
        foreach (var color in PlayerColors)
        {
            if (!_usedColors.Contains(color))
            {
                availableColors.Add(color);
            }
        }
        
        if (availableColors.Count > 0)
        {
            return availableColors[_random.Next(availableColors.Count)];
        }
        
        return PlayerColors[_random.Next(PlayerColors.Length)];
    }
    
    private string GetPlayerDisplayName(PlayerStatus status)
    {
        if (!string.IsNullOrEmpty(status.PlayerName))
        {
            return status.PlayerName;
        }
        
        return null;
    }
    
    private void ApplyExistingColors()
    {
        if (!Service.networkStarted)
            return;
            
        if (IsServer && remoteCharacters != null)
        {
            foreach (var kvp in remoteCharacters)
            {
                if (Service.playerStatuses.TryGetValue(kvp.Key, out var st))
                {
                    string playerId = st.EndPoint;
                    string playerName = GetPlayerDisplayName(st);
                    GameObject playerObject = kvp.Value;

                    if (_playerColors.TryGetValue(playerId, out var color))
                    {
                        ApplyColorAndNameToPlayer(playerId, playerName, playerObject, color);
                    }
                    else
                    {
                        AssignRandomColorsToPlayers();
                        return;
                    }
                }
            }
        }
        else if (clientRemoteCharacters != null)
        {
            foreach (var kvp in clientRemoteCharacters)
            {
                string playerId = kvp.Key;
                GameObject playerObject = kvp.Value;
                
                string playerName = null;
                if (Service.clientPlayerStatuses != null && Service.clientPlayerStatuses.TryGetValue(playerId, out var st))
                {
                    playerName = GetPlayerDisplayName(st);
                }

                if (_playerColors.TryGetValue(playerId, out var color))
                {
                    ApplyColorAndNameToPlayer(playerId, playerName, playerObject, color);
                }
                else
                {
                    AssignRandomColorsToPlayers();
                    return;
                }
            }
        }
        
        var localChar = LevelManager.Instance?.MainCharacter;
        if (localChar != null)
        {
            var localHealth = localChar.GetComponentInChildren<Health>(true);
            if (localHealth != null)
            {
                string localPlayerName = null;
                if (Service.TransportMode == NetworkTransportMode.SteamP2P)
                {
                    localPlayerName = Service.localPlayerStatus?.PlayerName;
                }
                ApplyHealthBarColorAndNameAsync(localHealth, LocalPlayerColor, localPlayerName).Forget();
            }
        }
    }

    private void ApplyColorToPlayer(string playerId, GameObject playerObject, Color color)
    {
        if (playerObject == null) return;

        var remoteTag = playerObject.GetComponent<RemoteReplicaTag>();
        if (remoteTag == null)
        {
            return;
        }

        Health health = playerObject.GetComponentInChildren<Health>(true);
        if (health != null)
        {
            ApplyHealthBarColorAsync(health, color).Forget();
        }
    }
    
    private void ApplyColorAndNameToPlayer(string playerId, string playerName, GameObject playerObject, Color color)
    {
        if (playerObject == null) return;

        var remoteTag = playerObject.GetComponent<RemoteReplicaTag>();
        if (remoteTag == null)
        {
            return;
        }

        Health health = playerObject.GetComponentInChildren<Health>(true);
        if (health != null)
        {
            ApplyHealthBarColorAndNameAsync(health, color, playerName).Forget();
        }
    }

    private async UniTaskVoid ApplyHealthBarColorAsync(Health health, Color color)
    {
        if (health == null) return;

        HealthBar bar = await ResolveHealthBarAsync(health);
        if (bar != null)
        {
            ApplyHealthBarColor(bar, color);
        }
    }
    
    private async UniTaskVoid ApplyHealthBarColorAndNameAsync(Health health, Color color, string playerName)
    {
        if (health == null) return;

        HealthBar bar = await ResolveHealthBarAsync(health);
        if (bar != null)
        {
            ApplyHealthBarColor(bar, color);
            if (!string.IsNullOrEmpty(playerName))
            {
                ApplyHealthBarName(bar, playerName);
            }
        }
    }

    private async UniTask<HealthBar> ResolveHealthBarAsync(Health health)
    {
        if (health == null) return null;

        if (_healthBarCache.TryGetValue(health, out var cached) && cached != null)
        {
            return cached;
        }

        for (int i = 0; i < 30; i++)
        {
            try
            {
                var hbm = HealthBarManager.Instance;
                if (hbm != null && _getActiveHealthBarMethod != null)
                {
                    var bar = _getActiveHealthBarMethod.Invoke(hbm, new object[] { health }) as HealthBar;
                    
                    if (bar != null)
                    {
                        _healthBarCache[health] = bar;
                        return bar;
                    }
                }
            }
            catch
            {
            }

            try
            {
                health.showHealthBar = true;
                health.RequestHealthBar();
            }
            catch
            {
            }

            await UniTask.Delay(100);
        }

        return null;
    }

    private void ApplyHealthBarColor(HealthBar bar, Color color)
    {
        if (bar == null) return;

        try
        {
            if (_fillField != null)
            {
                var fillImage = _fillField.GetValue(bar) as UnityEngine.UI.Image;
                if (fillImage != null)
                {
                    // 强制设置颜色，覆盖游戏的治疗/伤害动画
                    fillImage.color = color;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlayerColorManager] Failed to apply color: {ex.Message}");
        }
    }
    
    private void ApplyHealthBarName(HealthBar bar, string playerName)
    {
        if (bar == null) return;

        try
        {
            var healthBarType = typeof(HealthBar);
            var nameTextField = AccessTools.Field(healthBarType, "nameText");
            
            if (nameTextField != null)
            {
                var nameText = nameTextField.GetValue(bar) as TMPro.TextMeshProUGUI;
                if (nameText != null && !string.IsNullOrEmpty(playerName))
                {
                    nameText.text = playerName;
                    nameText.gameObject.SetActive(true);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlayerColorManager] Failed to apply name: {ex.Message}");
        }
    }

    public void ForceUpdatePlayerColor(string playerId, GameObject playerObject)
    {
        if (!_playerColors.ContainsKey(playerId))
        {
            Color randomColor = GetAvailableColor();
            _playerColors[playerId] = randomColor;
            _usedColors.Add(randomColor);
            Debug.Log($"[PlayerColorManager] Force assigned color to player {playerId}: {randomColor}");
        }
        
        string playerName = null;
        if (IsServer && Service.playerStatuses != null)
        {
            foreach (var kvp in Service.playerStatuses)
            {
                if (kvp.Value.EndPoint == playerId)
                {
                    playerName = GetPlayerDisplayName(kvp.Value);
                    break;
                }
            }
        }
        else if (Service.clientPlayerStatuses != null && Service.clientPlayerStatuses.TryGetValue(playerId, out var st))
        {
            playerName = GetPlayerDisplayName(st);
        }
        
        ApplyColorAndNameToPlayer(playerId, playerName, playerObject, _playerColors[playerId]);
    }

    public void ClearPlayerColor(string playerId)
    {
        if (_playerColors.TryGetValue(playerId, out var color))
        {
            _usedColors.Remove(color);
            _playerColors.Remove(playerId);
        }
    }

    public Color GetPlayerColor(string playerId)
    {
        return _playerColors.TryGetValue(playerId, out var color) ? color : Color.white;
    }
}
