using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Scenes;
using Duckov.Utilities;
using Duckov.Weathers;
using LiteNetLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public class Weather
{
    private const float ClockBroadcastInterval = 1.0f;
    private const float WeatherBroadcastInterval = 2.0f;
    private const int LootChunkSize = 80;
    private const int DoorChunkSize = 80;
    private const float LootStateBroadcastInterval = 35f;

    private readonly List<(int key, bool state)> _lootSnapshotBuffer = new();
    private readonly List<(int key, bool closed)> _doorSnapshotBuffer = new();

    private float _clockTimer;
    private float _weatherTimer;
    private float _lootSyncTimer;

    private float _clientResyncTimer;
    private bool _clientClockSynced;
    private bool _clientWeatherSynced;

    internal static StormSnapshot LastStormSnapshot { get; private set; } = StormSnapshot.Empty;

    private long _lastDay = long.MinValue;
    private double _lastSeconds = double.MinValue;
    private float _lastTimeScale = float.NaN;

    private int _lastSeed = int.MinValue;
    private bool _lastForceWeather;
    private int _lastForceWeatherValue = int.MinValue;
    private int _lastCurrentWeather = int.MinValue;
    private byte _lastStormLevel = byte.MaxValue;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private bool networkStarted => Service != null && Service.networkStarted;

    public void Server_Update(float deltaTime)
    {
        if (!IsServer || !networkStarted) return;

        _clockTimer += deltaTime;
        if (_clockTimer >= ClockBroadcastInterval)
        {
            _clockTimer = 0f;
            SendClockState(null, false);
        }

        _weatherTimer += deltaTime;
        if (_weatherTimer >= WeatherBroadcastInterval)
        {
            _weatherTimer = 0f;
            SendWeatherState(null, false);
        }

        if (HasActiveClients())
        {
            _lootSyncTimer += deltaTime;
            if (_lootSyncTimer >= LootStateBroadcastInterval)
            {
                _lootSyncTimer = 0f;
                SendLootSnapshot(null);
            }
        }
        else
        {
            _lootSyncTimer = 0f;
        }
    }

    public void Client_RequestSnapshot()
    {
        if (IsServer || !networkStarted) return;

        _clientClockSynced = false;
        _clientWeatherSynced = false;
        _clientResyncTimer = 0f;

        var request = new EnvSnapshotRequestRpc();
        CoopTool.SendRpc(in request);
    }

    public void Client_Update(float deltaTime)
    {
        if (IsServer || !networkStarted) return;

        if (_clientClockSynced && _clientWeatherSynced) return;

        _clientResyncTimer += deltaTime;
        if (_clientResyncTimer >= 3f)
        {
            _clientResyncTimer = 0f;
            Client_RequestSnapshot();
        }
    }

    public void Server_HandleSnapshotRequest(RpcContext context)
    {
        if (!IsServer || context.Sender == null) return;

        SendClockState(context.Sender, true);
        SendWeatherState(context.Sender, true);
        SendLootSnapshot(context.Sender);
        SendDoorSnapshot(context.Sender);
        SendDestructibleSnapshot(context.Sender);
        SendExplosiveBarrelSnapshot(context.Sender);
    }

    public void Client_HandleClockState(EnvClockStateRpc message)
    {
        if (IsServer) return;

        try
        {
            var inst = GameClock.Instance;
            if (inst == null) return;

            AccessTools.Field(inst.GetType(), "days")?.SetValue(inst, message.Day);
            AccessTools.Field(inst.GetType(), "secondsOfDay")?.SetValue(inst, message.SecondsOfDay);

            try
            {
                inst.clockTimeScale = message.TimeScale;
            }
            catch
            {
            }

            typeof(GameClock).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, new object[] { 0f });

            _clientClockSynced = true;
            _clientResyncTimer = 0f;
        }
        catch
        {
        }
    }

    public void Client_HandleWeatherState(EnvWeatherStateRpc message)
    {
        if (IsServer) return;

        try
        {
            var wm = WeatherManager.Instance;
            if (wm != null && message.Seed != -1)
            {
                AccessTools.Field(wm.GetType(), "seed")?.SetValue(wm, message.Seed);
                wm.GetType().GetMethod("SetupModules", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(wm, null);
                AccessTools.Field(wm.GetType(), "_weatherDirty")?.SetValue(wm, true);
            }
        }
        catch
        {
        }

        try
        {
            WeatherManager.SetForceWeather(message.ForceWeather, (Duckov.Weathers.Weather)message.ForceWeatherValue);
        }
        catch
        {
        }

        LastStormSnapshot = new StormSnapshot
        {
            HasData = true,
            StormLevel = message.StormLevel,
            CurrentWeather = (Duckov.Weathers.Weather)message.CurrentWeather,
            StormEtaSeconds = message.StormEtaSeconds,
            StormIOverSeconds = message.StormIOverSeconds,
            StormIIOverSeconds = message.StormIIOverSeconds,
            StormSleepPercent = message.StormSleepPercent,
            StormRemainPercent = message.StormRemainPercent
        };

        _clientWeatherSynced = true;
        _clientResyncTimer = 0f;
    }

    public void Client_HandleLootChunk(EnvLootChunkRpc message)
    {
        if (IsServer) return;
        LootNet.Client_ApplyLootVisibilityChunk(message.Keys, message.States, message.Reset);
    }

    public void Client_HandleDoorChunk(EnvDoorChunkRpc message)
    {
        if (IsServer) return;

        var keys = message.Keys;
        var states = message.ClosedStates;
        if (keys == null || states == null) return;

        var count = Math.Min(keys.Length, states.Length);
        if (count <= 0) return;

        try
        {
            var core = MultiSceneCore.Instance;
            for (var i = 0; i < count; i++)
            {
                var key = keys[i];
                var closed = states[i];

                if (core?.inLevelData != null)
                    core.inLevelData[key] = closed;

                COOPManager.Door.Client_ApplyDoorState(key, closed);
            }
        }
        catch
        {
        }
    }

    public void Client_HandleDestructibleState(EnvDestructibleStateRpc message)
    {
        if (IsServer) return;
        COOPManager.destructible?.Client_ApplyDestructibleSnapshot(message.DeadIds, message.Reset);
    }

    private bool TrySampleClock(out long day, out double seconds, out float timeScale)
    {
        day = 0;
        seconds = 0;
        timeScale = 60f;

        try
        {
            day = GameClock.Day;
            seconds = GameClock.TimeOfDay.TotalSeconds;
        }
        catch
        {
            return false;
        }

        try
        {
            var inst = GameClock.Instance;
            if (inst != null)
                timeScale = inst.clockTimeScale;
        }
        catch
        {
        }

        return true;
    }

    private bool TrySampleWeather(out int seed, out bool forceWeather, out int forceWeatherValue,
        out int currentWeather, out byte stormLevel)
    {
        seed = -1;
        forceWeather = false;
        forceWeatherValue = (int)Duckov.Weathers.Weather.Sunny;
        currentWeather = (int)Duckov.Weathers.Weather.Sunny;
        stormLevel = 0;

        var wm = WeatherManager.Instance;
        if (wm == null) return false;

        try
        {
            seed = (int)AccessTools.Field(wm.GetType(), "seed").GetValue(wm);
        }
        catch
        {
        }

        try
        {
            forceWeather = (bool)AccessTools.Field(wm.GetType(), "forceWeather").GetValue(wm);
        }
        catch
        {
        }

        try
        {
            forceWeatherValue = (int)AccessTools.Field(wm.GetType(), "forceWeatherValue").GetValue(wm);
        }
        catch
        {
        }

        try
        {
            currentWeather = (int)WeatherManager.GetWeather();
        }
        catch
        {
        }

        try
        {
            stormLevel = (byte)wm.Storm.GetStormLevel(GameClock.Now);
        }
        catch
        {
        }

        return true;
    }

    private void SendClockState(NetPeer target, bool force)
    {
        if (!TrySampleClock(out var day, out var seconds, out var timeScale)) return;

        if (!force)
        {
            if (day == _lastDay && Math.Abs(seconds - _lastSeconds) < 0.01 && Math.Abs(timeScale - _lastTimeScale) < 0.001)
                return;
        }

        _lastDay = day;
        _lastSeconds = seconds;
        _lastTimeScale = timeScale;

        var rpc = new EnvClockStateRpc
        {
            Day = day,
            SecondsOfDay = seconds,
            TimeScale = timeScale
        };

        if (target != null)
            CoopTool.SendRpcTo(target, in rpc);
        else
            CoopTool.SendRpc(in rpc);
    }

    private void SendWeatherState(NetPeer target, bool force)
    {
        if (!TrySampleWeather(out var seed, out var forceWeather, out var forceWeatherValue,
                out var currentWeather, out var stormLevel))
        {
            if (!force) return;
        }

        var stormEtaSeconds = -1d;
        var stormIOverSeconds = -1d;
        var stormIIOverSeconds = -1d;
        var stormSleepPercent = 0f;
        var stormRemainPercent = 0f;

        try
        {
            var wm = WeatherManager.Instance;
            var now = GameClock.Now;
            stormEtaSeconds = wm?.Storm.GetStormETA(now).TotalSeconds ?? -1d;
            stormIOverSeconds = wm?.Storm.GetStormIOverETA(now).TotalSeconds ?? -1d;
            stormIIOverSeconds = wm?.Storm.GetStormIIOverETA(now).TotalSeconds ?? -1d;
            stormSleepPercent = wm != null ? wm.Storm.GetSleepPercent(now) : 0f;
            stormRemainPercent = wm != null ? wm.Storm.GetStormRemainPercent(now) : 0f;
        }
        catch
        {
        }

        if (!force)
        {
            if (seed == _lastSeed && forceWeather == _lastForceWeather &&
                forceWeatherValue == _lastForceWeatherValue && currentWeather == _lastCurrentWeather &&
                stormLevel == _lastStormLevel)
                return;
        }

        _lastSeed = seed;
        _lastForceWeather = forceWeather;
        _lastForceWeatherValue = forceWeatherValue;
        _lastCurrentWeather = currentWeather;
        _lastStormLevel = stormLevel;

        var rpc = new EnvWeatherStateRpc
        {
            Seed = seed,
            ForceWeather = forceWeather,
            ForceWeatherValue = forceWeatherValue,
            CurrentWeather = currentWeather,
            StormLevel = stormLevel,
            StormEtaSeconds = stormEtaSeconds,
            StormIOverSeconds = stormIOverSeconds,
            StormIIOverSeconds = stormIIOverSeconds,
            StormSleepPercent = stormSleepPercent,
            StormRemainPercent = stormRemainPercent
        };

        if (target != null)
            CoopTool.SendRpcTo(target, in rpc);
        else
            CoopTool.SendRpc(in rpc);
    }

    private void SendLootSnapshot(NetPeer target)
    {
        _lootSnapshotBuffer.Clear();

        foreach (var entry in CoopSyncDatabase.Loot.Entries)
        {
            if (entry == null) continue;
            var key = entry.PositionKey;
            if (key == 0) continue;

            GameObject go = null;
            try
            {
                if (entry.Loader)
                    go = entry.Loader.gameObject;
                else if (entry.Lootbox)
                    go = entry.Lootbox.gameObject;
            }
            catch
            {
                go = null;
            }

            if (go == null) continue;

            var active = false;
            try
            {
                active = go.activeSelf;
            }
            catch
            {
            }

            _lootSnapshotBuffer.Add((key, active));
        }

        if (_lootSnapshotBuffer.Count == 0)
        {
            var empty = new EnvLootChunkRpc
            {
                Reset = true,
                Keys = Array.Empty<int>(),
                States = Array.Empty<bool>()
            };
            if (target != null)
                CoopTool.SendRpcTo(target, in empty);
            else
                CoopTool.SendRpc(in empty);
            return;
        }

        for (var offset = 0; offset < _lootSnapshotBuffer.Count; offset += LootChunkSize)
        {
            var count = Math.Min(LootChunkSize, _lootSnapshotBuffer.Count - offset);
            var keys = new int[count];
            var states = new bool[count];

            for (var i = 0; i < count; i++)
            {
                var tuple = _lootSnapshotBuffer[offset + i];
                keys[i] = tuple.key;
                states[i] = tuple.state;
            }

            var rpc = new EnvLootChunkRpc
            {
                Reset = offset == 0,
                Keys = keys,
                States = states
            };

            if (target != null)
                CoopTool.SendRpcTo(target, in rpc);
            else
                CoopTool.SendRpc(in rpc);
        }
    }

    private void SendDoorSnapshot(NetPeer target)
    {
        _doorSnapshotBuffer.Clear();

        var registry = CoopSyncDatabase.Environment.Doors;

        var entries = registry.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry == null) continue;

            var door = entry.Door;
            if (!door) continue;

            registry.Register(door);

            var key = entry.Key;
            if (key == 0)
                key = Door.TryGetDoorKey(door);

            if (key == 0) continue;

            var closed = true;
            try
            {
                closed = !door.IsOpen;
            }
            catch
            {
            }

            _doorSnapshotBuffer.Add((key, closed));
        }

        if (_doorSnapshotBuffer.Count == 0) return;

        for (var offset = 0; offset < _doorSnapshotBuffer.Count; offset += DoorChunkSize)
        {
            var count = Math.Min(DoorChunkSize, _doorSnapshotBuffer.Count - offset);
            var keys = new int[count];
            var states = new bool[count];

            for (var i = 0; i < count; i++)
            {
                var tuple = _doorSnapshotBuffer[offset + i];
                keys[i] = tuple.key;
                states[i] = tuple.closed;
            }

            var rpc = new EnvDoorChunkRpc
            {
                Reset = offset == 0,
                Keys = keys,
                ClosedStates = states
            };

            CoopTool.SendRpcTo(target, in rpc);
        }
    }

    private void SendDestructibleSnapshot(NetPeer target)
    {
        var destructible = COOPManager.destructible;
        if (destructible == null)
        {
            var empty = new EnvDestructibleStateRpc
            {
                Reset = true,
                DeadIds = Array.Empty<uint>()
            };
            CoopTool.SendRpcTo(target, in empty);
            return;
        }

        var source = destructible._deadDestructibleIds;
        var count = source?.Count ?? 0;
        if (count == 0)
        {
            var empty = new EnvDestructibleStateRpc
            {
                Reset = true,
                DeadIds = Array.Empty<uint>()
            };
            CoopTool.SendRpcTo(target, in empty);
            return;
        }

        var ids = new uint[count];
        var idx = 0;
        foreach (var id in source)
            ids[idx++] = id;

        var rpc = new EnvDestructibleStateRpc
        {
            Reset = true,
            DeadIds = ids
        };

        CoopTool.SendRpcTo(target, in rpc);
    }

    private void SendExplosiveBarrelSnapshot(NetPeer target)
    {
        var barrels = COOPManager.ExplosiveBarrels;
        if (barrels == null)
        {
            var empty = new EnvExplosiveOilBarrelStateRpc
            {
                Reset = true,
                Ids = Array.Empty<uint>(),
                ActiveStates = Array.Empty<bool>()
            };
            CoopTool.SendRpcTo(target, in empty);
            return;
        }

        barrels.Server_BroadcastSnapshot(target);
    }

    private bool HasActiveClients()
    {
        var manager = Service?.netManager;
        return manager != null && manager.ConnectedPeersCount > 0;
    }
}

internal struct StormSnapshot
{
    public static readonly StormSnapshot Empty = new()
    {
        HasData = false,
        StormLevel = byte.MaxValue,
        CurrentWeather = Duckov.Weathers.Weather.Sunny
    };

    public bool HasData;
    public byte StormLevel;
    public Duckov.Weathers.Weather CurrentWeather;
    public double StormEtaSeconds;
    public double StormIOverSeconds;
    public double StormIIOverSeconds;
    public float StormSleepPercent;
    public float StormRemainPercent;
}

