// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.MiniMaps;
using Duckov.Scenes;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public class ExitSyncService
{
    private static readonly FieldInfo IconField = AccessTools.Field(typeof(global::ExitCreator), "icon");
    private static readonly FieldInfo IconColorField = AccessTools.Field(typeof(global::ExitCreator), "iconColor");
    private static readonly FieldInfo ShadowColorField = AccessTools.Field(typeof(global::ExitCreator), "shadowColor");
    private static readonly FieldInfo ShadowDistanceField = AccessTools.Field(typeof(global::ExitCreator), "shadowDistance");

    private readonly List<ExitSnapshotEntry> _serverSnapshot = new();
    private readonly List<NetPeer> _serverPendingPeers = new();
    private string _serverSceneId;
    private bool _serverHasSnapshot;

    private readonly List<ExitSnapshotEntry> _clientPending = new();
    private readonly HashSet<int> _clientSpawnedKeys = new();
    private readonly List<GameObject> _clientSpawnedExits = new();
    private readonly List<GameObject> _clientSpawnedIcons = new();
    private global::ExitCreator _clientCreator;
    private ExitTemplate _clientTemplate;
    private string _clientSceneId;
    private float _clientNextRequestTime;
    private bool _clientSynced;

    private struct ExitTemplate
    {
        public GameObject Prefab;
        public Sprite Icon;
        public Color IconColor;
        public Color ShadowColor;
        public float ShadowDistance;
        public string ExitNameKey;
    }

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private bool NetworkStarted => Service != null && Service.networkStarted;

    public void RegisterCreator(global::ExitCreator creator)
    {
        if (!creator) return;

        if (IsServer)
        {
            _serverSceneId = GetCurrentSceneId();
        }
        else
        {
            _clientCreator = creator;
            _clientTemplate = CaptureTemplate(creator);
        }
    }

    public void Server_OnSpawnStarting(global::ExitCreator creator)
    {
        if (!IsServer) return;
        _serverSceneId = GetCurrentSceneId();
        _serverSnapshot.Clear();
        _serverHasSnapshot = false;
    }

    public void Server_RecordExit(global::ExitCreator creator, Vector3 position, int sceneBuildIndex)
    {
        if (!IsServer) return;

        var entry = new ExitSnapshotEntry
        {
            Position = position,
            SceneBuildIndex = sceneBuildIndex
        };

        _serverSnapshot.Add(entry);
    }

    public void Server_OnSpawnFinished(global::ExitCreator creator)
    {
        if (!IsServer) return;

        _serverHasSnapshot = _serverSnapshot.Count > 0;
        if (!_serverHasSnapshot) return;

        if (Service?.netManager != null && Service.netManager.ConnectedPeersCount > 0)
            Server_BroadcastSnapshot(true);

        FlushPendingPeers();
    }

    public void Server_HandleSnapshotRequest(RpcContext context, in EnvExitSnapshotRequestRpc request)
    {
        if (!IsServer || context.Sender == null) return;

        if (!string.IsNullOrEmpty(request.SceneId) &&
            !string.Equals(request.SceneId, _serverSceneId, StringComparison.Ordinal))
            return;

        if (_serverHasSnapshot)
        {
            Server_SendSnapshot(context.Sender, true);
            return;
        }

        if (!_serverPendingPeers.Contains(context.Sender))
            _serverPendingPeers.Add(context.Sender);
    }

    private void FlushPendingPeers()
    {
        if (_serverPendingPeers.Count == 0 || !_serverHasSnapshot) return;

        for (var i = 0; i < _serverPendingPeers.Count; i++)
        {
            var peer = _serverPendingPeers[i];
            if (peer == null) continue;
            Server_SendSnapshot(peer, true);
        }

        _serverPendingPeers.Clear();
    }

    private void Server_SendSnapshot(NetPeer target, bool reset)
    {
        if (!IsServer || target == null) return;

        var rpc = new EnvExitSnapshotRpc
        {
            Reset = reset,
            SceneId = _serverSceneId ?? string.Empty,
            Entries = _serverSnapshot.Count == 0 ? Array.Empty<ExitSnapshotEntry>() : _serverSnapshot.ToArray()
        };

        CoopTool.SendRpcTo(target, in rpc);
    }

    private void Server_BroadcastSnapshot(bool reset)
    {
        if (!IsServer || _serverSnapshot.Count == 0) return;

        var rpc = new EnvExitSnapshotRpc
        {
            Reset = reset,
            SceneId = _serverSceneId ?? string.Empty,
            Entries = _serverSnapshot.ToArray()
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Client_OnSpawnBlocked(global::ExitCreator creator)
    {
        if (IsServer) return;

        if (creator)
        {
            _clientCreator = creator;
            _clientTemplate = CaptureTemplate(creator);
        }

        _clientSynced = false;
        _clientNextRequestTime = 0f;
        Client_RequestSnapshot(true);
    }

    public void Client_RequestSnapshot(bool force)
    {
        if (IsServer || !NetworkStarted) return;

        var sceneId = GetCurrentSceneId();
        if (string.IsNullOrEmpty(sceneId)) return;

        if (!force)
        {
            if (_clientSynced && string.Equals(sceneId, _clientSceneId, StringComparison.Ordinal))
                return;

            if (Time.unscaledTime < _clientNextRequestTime)
                return;
        }

        _clientSceneId = sceneId;
        _clientSynced = false;
        _clientNextRequestTime = Time.unscaledTime + 3f;

        var request = new EnvExitSnapshotRequestRpc
        {
            SceneId = sceneId
        };

        CoopTool.SendRpc(in request);
    }

    public void Client_Update(float deltaTime)
    {
        if (IsServer || !NetworkStarted) return;

        if (!_clientSynced && Time.unscaledTime >= _clientNextRequestTime)
            Client_RequestSnapshot(false);

        if (_clientPending.Count > 0)
            Client_TrySpawnPending();
    }

    public void Client_ApplySnapshot(in EnvExitSnapshotRpc message)
    {
        if (IsServer) return;

        if (!string.IsNullOrEmpty(message.SceneId) && !IsSceneMatch(message.SceneId))
            return;

        if (message.Reset)
            Client_ClearSpawned();

        _clientPending.Clear();
        if (message.Entries != null && message.Entries.Length > 0)
            _clientPending.AddRange(message.Entries);

        _clientSynced = true;
        _clientSceneId = !string.IsNullOrEmpty(message.SceneId) ? message.SceneId : GetCurrentSceneId();
        _clientNextRequestTime = Time.unscaledTime + 3f;

        Client_TrySpawnPending();
    }

    private void Client_TrySpawnPending()
    {
        if (_clientPending.Count == 0) return;

        var creator = ResolveCreator();
        if (!creator) return;

        if (_clientTemplate.Prefab == null)
            _clientTemplate = CaptureTemplate(creator);

        if (_clientTemplate.Prefab == null) return;

        for (var i = 0; i < _clientPending.Count; i++)
        {
            var entry = _clientPending[i];
            var key = ComputeKey(in entry);
            if (_clientSpawnedKeys.Contains(key)) continue;

            var exitGo = Object.Instantiate(_clientTemplate.Prefab, entry.Position, Quaternion.identity);
            if (MultiSceneCore.Instance)
                MultiSceneCore.MoveToActiveWithScene(exitGo, entry.SceneBuildIndex);

            _clientSpawnedExits.Add(exitGo);

            var poi = SpawnMapElement(_clientTemplate, in entry);
            if (poi)
                _clientSpawnedIcons.Add(poi.gameObject);

            _clientSpawnedKeys.Add(key);
        }

        _clientPending.Clear();
    }

    private static int ComputeKey(in ExitSnapshotEntry entry)
    {
        var scaled = entry.Position * 100f;
        var rounded = new Vector3Int(
            Mathf.RoundToInt(scaled.x),
            Mathf.RoundToInt(scaled.y),
            Mathf.RoundToInt(scaled.z));

        unchecked
        {
            var hash = rounded.GetHashCode();
            hash = (hash * 486187739) ^ entry.SceneBuildIndex;
            return hash;
        }
    }

    private SimplePointOfInterest SpawnMapElement(ExitTemplate template, in ExitSnapshotEntry entry)
    {
        if (MultiSceneCore.Instance == null) return null;

        var poi = new GameObject("MapElement").AddComponent<SimplePointOfInterest>();
        poi.transform.position = entry.Position;
        poi.Color = template.IconColor;
        poi.ShadowColor = template.ShadowColor;
        poi.ShadowDistance = template.ShadowDistance;
        poi.IsArea = false;
        poi.ScaleFactor = 1f;

        var sceneId = SceneInfoCollection.GetSceneID(entry.SceneBuildIndex);
        poi.Setup(template.Icon, template.ExitNameKey, false, sceneId);
        SceneManager.MoveGameObjectToScene(poi.gameObject, MultiSceneCore.MainScene.Value);
        return poi;
    }

    private global::ExitCreator ResolveCreator()
    {
        if (_clientCreator)
            return _clientCreator;

        var found = Object.FindObjectOfType<global::ExitCreator>(true);
        if (found)
        {
            _clientCreator = found;
            _clientTemplate = CaptureTemplate(found);
        }

        return _clientCreator;
    }

    private static ExitTemplate CaptureTemplate(global::ExitCreator creator)
    {
        var template = new ExitTemplate
        {
            Prefab = creator ? creator.exitPrefab : null,
            ExitNameKey = creator ? creator.exitNameKey : string.Empty,
            Icon = creator && IconField != null ? IconField.GetValue(creator) as Sprite : null,
            IconColor = creator && IconColorField != null ? (Color)IconColorField.GetValue(creator) : Color.white,
            ShadowColor = creator && ShadowColorField != null ? (Color)ShadowColorField.GetValue(creator) : Color.white,
            ShadowDistance = creator && ShadowDistanceField != null ? (float)ShadowDistanceField.GetValue(creator) : 0f
        };

        return template;
    }

    public void Reset()
    {
        _serverSnapshot.Clear();
        _serverPendingPeers.Clear();
        _serverHasSnapshot = false;
        _serverSceneId = null;

        _clientPending.Clear();
        _clientSpawnedKeys.Clear();
        _clientSceneId = null;
        _clientSynced = false;
        _clientNextRequestTime = 0f;

        //for (var i = 0; i < _clientSpawnedExits.Count; i++)
        //    if (_clientSpawnedExits[i]) Object.Destroy(_clientSpawnedExits[i]);
        //_clientSpawnedExits.Clear();

        //for (var i = 0; i < _clientSpawnedIcons.Count; i++)
        //    if (_clientSpawnedIcons[i]) Object.Destroy(_clientSpawnedIcons[i]);
        //_clientSpawnedIcons.Clear();

        _clientCreator = null;
        _clientTemplate = default;
    }

    private void Client_ClearSpawned()
    {
        for (var i = 0; i < _clientSpawnedExits.Count; i++)
            if (_clientSpawnedExits[i]) Object.Destroy(_clientSpawnedExits[i]);
        _clientSpawnedExits.Clear();

        for (var i = 0; i < _clientSpawnedIcons.Count; i++)
            if (_clientSpawnedIcons[i]) Object.Destroy(_clientSpawnedIcons[i]);
        _clientSpawnedIcons.Clear();

        _clientSpawnedKeys.Clear();
    }

    private static string GetCurrentSceneId()
    {
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                var idFromBuild = SceneInfoCollection.GetSceneID(scene.buildIndex);
                if (!string.IsNullOrEmpty(idFromBuild))
                    return idFromBuild;

                if (!string.IsNullOrEmpty(scene.name))
                    return scene.name;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private bool IsSceneMatch(string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId)) return true;
        var current = GetCurrentSceneId();
        return string.Equals(current, sceneId, StringComparison.Ordinal);
    }
}
