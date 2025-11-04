using System.Collections.Generic;
using System.Reflection;
using Duckov.MiniMaps.UI;
using Duckov.UI;
using UnityEngine.SceneManagement;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public class TeleportManager : MonoBehaviour
{
    public static TeleportManager Instance { get; private set; }

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    
    private bool _rpcRegistered = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    
    private void Start()
    {
        RegisterRPCs();
    }
    
    private void RegisterRPCs()
    {
        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
        {
            Debug.LogWarning("[TeleportManager] HybridRPCManager not found, RPC mode disabled");
            return;
        }

        rpcManager.RegisterRPC("TeleportRequest", OnRPC_TeleportRequest);
        rpcManager.RegisterRPC("TeleportExecute", OnRPC_TeleportExecute);

        _rpcRegistered = true;
        Debug.Log("[TeleportManager] All RPCs registered");
        Debug.Log("[TeleportManager] Teleport hotkey: T (when map is open)");
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

    private void FixZoneTriggerExit(CharacterMainControl mainCharacter)
    {
        try
        {
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] rootGameObjects = activeScene.GetRootGameObjects();
            if (rootGameObjects == null || rootGameObjects.Length == 0)
                return;

            foreach (GameObject gameObject in rootGameObjects)
            {
                Zone[] zonesInChildren = gameObject.GetComponentsInChildren<Zone>();
                if (zonesInChildren != null && zonesInChildren.Length != 0)
                {
                    foreach (Zone zone in zonesInChildren)
                    {
                        var healths = zone.Healths;
                        if (healths != null && healths.Count != 0)
                        {
                            healths.Remove(mainCharacter.Health);
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[TeleportManager] Zone trigger cleanup failed: {ex.Message}");
        }
    }

    public async void TeleportFromMap(Vector3 targetPos)
    {
        CharacterMainControl mainCharacter = LevelManager.Instance.MainCharacter;
        if (mainCharacter == null)
        {
            Debug.LogWarning("[TeleportManager] MainCharacter is null, cannot teleport");
            return;
        }

        Vector3 fitPos;
        if (!TryGetFitPosition(targetPos, out fitPos))
        {
            Debug.LogWarning($"[TeleportManager] No valid ground found at {targetPos}");
            mainCharacter.PopText(CoopLocalization.Get("ui.teleport.noGround"), -1f);
            return;
        }

        Debug.Log($"[TeleportManager] Starting map teleport to {fitPos}");
        
        try
        {
            await BlackScreen.ShowAndReturnTask(null, 0f, 0.5f);
            Debug.Log("[TeleportManager] BlackScreen shown");
            
            ExecuteTeleport(mainCharacter, fitPos);
            Debug.Log("[TeleportManager] Local teleport executed");
            
            await BlackScreen.HideAndReturnTask(null, 0f, 0.5f);
            Debug.Log("[TeleportManager] BlackScreen hidden");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TeleportManager] Teleport failed: {ex.Message}");
        }

        if (!_rpcRegistered)
            return;

        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
            return;

        if (rpcManager.IsServer)
        {
            rpcManager.CallRPC("TeleportExecute", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(fitPos.x);
                writer.Put(fitPos.y);
                writer.Put(fitPos.z);
            }, DeliveryMethod.ReliableOrdered);
            
            Debug.Log($"[TeleportManager] Server broadcast teleport to all clients: {fitPos}");
        }
        else if (rpcManager.IsClient)
        {
            rpcManager.CallRPC("TeleportRequest", Net.HybridP2P.RPCTarget.Server, 0, (writer) =>
            {
                writer.Put(fitPos.x);
                writer.Put(fitPos.y);
                writer.Put(fitPos.z);
            }, DeliveryMethod.ReliableOrdered);
            
            Debug.Log($"[TeleportManager] Client sent teleport request to server: {fitPos}");
        }
    }

    public async void TeleportToPlayer(string playerId)
    {
        GameObject targetPlayer = null;

        if (IsServer && remoteCharacters != null)
        {
            foreach (var kvp in remoteCharacters)
            {
                if (kvp.Value != null && kvp.Key.Id.ToString() == playerId)
                {
                    targetPlayer = kvp.Value;
                    break;
                }
            }
        }
        else if (!IsServer && clientRemoteCharacters != null)
        {
            if (clientRemoteCharacters.TryGetValue(playerId, out var player))
            {
                targetPlayer = player;
            }
        }

        if (targetPlayer == null)
        {
            Debug.LogWarning($"[TeleportManager] Target player not found: {playerId}");
            return;
        }

        Vector3 targetPos = targetPlayer.transform.position;
        Vector3 fitPos;
        if (!TryGetFitPosition(targetPos, out fitPos))
        {
            fitPos = targetPos;
        }

        CharacterMainControl mainCharacter = LevelManager.Instance.MainCharacter;
        if (mainCharacter == null)
            return;

        Debug.Log($"[TeleportManager] Starting teleport to player {playerId} at {fitPos}");
        
        try
        {
            await BlackScreen.ShowAndReturnTask(null, 0f, 0.5f);
            Debug.Log("[TeleportManager] BlackScreen shown for player teleport");
            
            ExecuteTeleport(mainCharacter, fitPos);
            Debug.Log("[TeleportManager] Local teleport to player executed");
            
            await BlackScreen.HideAndReturnTask(null, 0f, 0.5f);
            Debug.Log("[TeleportManager] BlackScreen hidden after player teleport");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TeleportManager] Teleport to player failed: {ex.Message}");
        }

        if (!_rpcRegistered)
            return;

        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
            return;

        if (rpcManager.IsServer)
        {
            rpcManager.CallRPC("TeleportExecute", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(fitPos.x);
                writer.Put(fitPos.y);
                writer.Put(fitPos.z);
            }, DeliveryMethod.ReliableOrdered);
            
            Debug.Log($"[TeleportManager] Server broadcast player teleport to all clients: {fitPos}");
        }
        else if (rpcManager.IsClient)
        {
            rpcManager.CallRPC("TeleportRequest", Net.HybridP2P.RPCTarget.Server, 0, (writer) =>
            {
                writer.Put(fitPos.x);
                writer.Put(fitPos.y);
                writer.Put(fitPos.z);
            }, DeliveryMethod.ReliableOrdered);
            
            Debug.Log($"[TeleportManager] Client sent player teleport request to server: {fitPos}");
        }
    }

    private void ExecuteTeleport(CharacterMainControl mainCharacter, Vector3 targetPos)
    {
        mainCharacter.SetPosition(targetPos);

        var interpolator = mainCharacter.gameObject.GetComponent<NetInterpolator>();
        if (interpolator != null)
        {
            interpolator.Push(targetPos, mainCharacter.transform.rotation);
        }

        FixZoneTriggerExit(mainCharacter);
    }

    private void OnRPC_TeleportRequest(long senderConnectionId, NetDataReader reader)
    {
        if (!IsServer)
            return;

        float x = reader.GetFloat();
        float y = reader.GetFloat();
        float z = reader.GetFloat();
        Vector3 targetPos = new Vector3(x, y, z);

        Debug.Log($"[TeleportManager] Received teleport request from {senderConnectionId}: {targetPos}");

        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
            return;

        rpcManager.CallRPC("TeleportExecute", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
        {
            writer.Put(x);
            writer.Put(y);
            writer.Put(z);
        }, DeliveryMethod.ReliableOrdered);
        
        Debug.Log($"[TeleportManager] Server forwarded teleport to all clients: {targetPos}");
    }

    private async void OnRPC_TeleportExecute(long senderConnectionId, NetDataReader reader)
    {
        float x = reader.GetFloat();
        float y = reader.GetFloat();
        float z = reader.GetFloat();
        Vector3 targetPos = new Vector3(x, y, z);

        CharacterMainControl mainCharacter = LevelManager.Instance.MainCharacter;
        if (mainCharacter == null)
            return;

        Debug.Log($"[TeleportManager] Received teleport execute from server: {targetPos}");
        
        try
        {
            await BlackScreen.ShowAndReturnTask(null, 0f, 0.5f);
            Debug.Log("[TeleportManager] BlackScreen shown for remote teleport");
            
            ExecuteTeleport(mainCharacter, targetPos);
            Debug.Log("[TeleportManager] Remote teleport executed");
            
            await BlackScreen.HideAndReturnTask(null, 0f, 0.5f);
            Debug.Log("[TeleportManager] BlackScreen hidden after remote teleport");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TeleportManager] Remote teleport failed: {ex.Message}");
        }
    }

    public Vector3? GetMouseWorldPosition()
    {
        MiniMapView miniMapView = MiniMapView.Instance;
        if (miniMapView == null || View.ActiveView != miniMapView)
            return null;

        var miniMapViewType = miniMapView.GetType();
        var miniMapDisplayField = miniMapViewType.GetField("display", BindingFlags.Instance | BindingFlags.NonPublic);
        
        if (miniMapDisplayField == null)
            return null;

        var miniMapDisplay = miniMapDisplayField.GetValue(miniMapView) as MiniMapDisplay;
        if (miniMapDisplay == null)
            return null;

        Vector3 targetPos;
        if (miniMapDisplay.TryConvertToWorldPosition(CharacterInputControl.Instance.inputManager.MousePos, out targetPos))
            return targetPos;
            
        return null;
    }
}
