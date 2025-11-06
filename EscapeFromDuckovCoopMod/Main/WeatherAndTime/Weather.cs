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

using System.Reflection;
using Duckov.Utilities;
using Duckov.Weathers;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public class Weather
{
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;

    private bool networkStarted => Service != null && Service.networkStarted;
    
    private static FieldInfo GetFieldSafe(Type type, string fieldName)
    {
        try
        {
            return type.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        }
        catch
        {
            return null;
        }
    }

    // ========== 环境同步：主机广播 ==========
    public void Server_BroadcastEnvSync(NetPeer target = null)
    {
        if (!IsServer || netManager == null) return;

        // 1) 采样主机的“当前天数 + 当天秒数 + 时钟倍率”
        var day = GameClock.Day; // 只读属性，取值 OK :contentReference[oaicite:6]{index=6}
        var secOfDay = GameClock.TimeOfDay.TotalSeconds; // 当天秒数（0~86300） :contentReference[oaicite:7]{index=7}
        var timeScale = 60f;
        try
        {
            timeScale = GameClock.Instance.clockTimeScale;
        }
        catch
        {
        } // 公有字段 :contentReference[oaicite:8]{index=8}

        // 2) 采样天气：seed / 强制天气开关和值 / 当前天气（兜底）/（冗余）风暴等级
        var wm = WeatherManager.Instance;
        var seed = -1;
        var forceWeather = false;
        var forceWeatherVal = (int)Duckov.Weathers.Weather.Sunny;
        var currentWeather = (int)Duckov.Weathers.Weather.Sunny;
        byte stormLevel = 0;

        if (wm != null)
        {
            try
            {
                var seedField = GetFieldSafe(wm.GetType(), "seed");
                if (seedField != null)
                {
                    seed = (int)seedField.GetValue(wm);
                }
            }
            catch
            {
            }

            try
            {
                var forceWeatherField = GetFieldSafe(wm.GetType(), "forceWeather");
                if (forceWeatherField != null)
                {
                    forceWeather = (bool)forceWeatherField.GetValue(wm);
                }
            }
            catch
            {
            } // 若字段名不同可改为属性读取

            try
            {
                var forceWeatherValField = GetFieldSafe(wm.GetType(), "forceWeatherValue");
                if (forceWeatherValField != null)
                {
                    forceWeatherVal = (int)forceWeatherValField.GetValue(wm);
                }
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
            } // 公共静态入口 :contentReference[oaicite:9]{index=9}

            try
            {
                stormLevel = (byte)wm.Storm.GetStormLevel(GameClock.Now);
            }
            catch
            {
            } // 基于 Now 计算 :contentReference[oaicite:10]{index=10}
        }

        var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null) return;

        long connectionId = 0;
        var rpcTarget = EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.AllClients;
        if (target != null)
        {
            connectionId = rpcManager.GetConnectionIdByPeer(target);
            if (connectionId == 0) return;
            rpcTarget = EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.TargetClient;
        }

        var lootBoxes = new List<(int k, bool on)>();
        try
        {
            var all = Object.FindObjectsOfType<LootBoxLoader>(true);
            foreach (var l in all)
            {
                if (!l || !l.gameObject) continue;
                var k = LootManager.Instance.ComputeLootKey(l.transform);
                var on = l.gameObject.activeSelf;
                lootBoxes.Add((k, on));
            }
        }
        catch
        {
        }

        var doors = new List<(int key, bool closed)>();
        var includeDoors = target != null;
        if (includeDoors)
        {
            try
            {
                var allDoors = Object.FindObjectsOfType<global::Door>(true);
                foreach (var d in allDoors)
                {
                    if (!d) continue;
                    var k = 0;
                    try
                    {
                        var doorField = GetFieldSafe(typeof(global::Door), "doorClosedDataKeyCached");
                        if (doorField != null)
                        {
                            k = (int)doorField.GetValue(d);
                        }
                    }
                    catch
                    {
                    }

                    if (k == 0) k = COOPManager.Door.ComputeDoorKey(d.transform);

                    bool closed;
                    try
                    {
                        closed = !d.IsOpen;
                    }
                    catch
                    {
                        closed = true;
                    }

                    doors.Add((k, closed));
                }
            }
            catch
            {
            }
        }

        rpcManager.CallRPC("EnvSyncState", rpcTarget, connectionId, w =>
        {
            w.Put(day);
            w.Put(secOfDay);
            w.Put(timeScale);
            w.Put(seed);
            w.Put(forceWeather);
            w.Put(forceWeatherVal);
            w.Put(currentWeather);
            w.Put(stormLevel);

            w.Put(lootBoxes.Count);
            foreach (var lb in lootBoxes)
            {
                w.Put(lb.k);
                w.Put(lb.on);
            }

            w.Put(doors.Count);
            foreach (var d in doors)
            {
                w.Put(d.key);
                w.Put(d.closed);
            }

            w.Put(COOPManager.destructible._deadDestructibleIds.Count);
            foreach (var id in COOPManager.destructible._deadDestructibleIds)
                w.Put(id);
        }, DeliveryMethod.ReliableOrdered);
    }


    // ========== 环境同步：客户端请求 ==========
    public void Client_RequestEnvSync()
    {
        if (IsServer) return;
        
        if (!networkStarted)
        {
            return;
        }

        var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null) return;

        rpcManager.CallRPC("EnvSyncRequest", EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.Server, 0, w =>
        {
        }, DeliveryMethod.Sequenced);
    }

    // ========== 环境同步：客户端应用 ==========
    public void Client_ApplyEnvSync(long day, double secOfDay, float timeScale, int seed, bool forceWeather, int forceWeatherVal, int currentWeather /*兜底*/,
        byte stormLevel /*冗余*/)
    {
        // 1) 绝对对时：直接改 GameClock 的私有字段（避免 StepTimeTil 无法回拨的问题）
        try
        {
            var inst = GameClock.Instance;
            if (inst != null)
            {
                GetFieldSafe(inst.GetType(), "days")?.SetValue(inst, day);
                GetFieldSafe(inst.GetType(), "secondsOfDay")?.SetValue(inst, secOfDay);
                try
                {
                    inst.clockTimeScale = timeScale;
                }
                catch
                {
                }

                // 触发一次 onGameClockStep（用 0 步长调用内部 Step，保证监听者能刷新）
                typeof(GameClock).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, new object[] { 0f });
            }
        }
        catch
        {
        }

        // 2) 天气随机种子：设到 WeatherManager，并让它把种子分发给子模块
        try
        {
            var wm = WeatherManager.Instance;
            if (wm != null && seed != -1)
            {
                GetFieldSafe(wm.GetType(), "seed")?.SetValue(wm, seed);
                wm.GetType().GetMethod("SetupModules", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(wm, null);
                GetFieldSafe(wm.GetType(), "_weatherDirty")?.SetValue(wm, true);
            }
        }
        catch
        {
        }

        // 3) 强制天气（兜底）：若主机处于强制状态，则客户端也强制到同一值
        try
        {
            WeatherManager.SetForceWeather(forceWeather, (Duckov.Weathers.Weather)forceWeatherVal); // 公共静态入口 :contentReference[oaicite:13]{index=13}
        }
        catch
        {
        }

        // 4) 无需专门同步风暴 ETA：基于 Now+seed，Storm.* 会得到一致的结果（UI 每 0.5s 刷新，见 TimeOfDayDisplay） :contentReference[oaicite:14]{index=14}
    }
}