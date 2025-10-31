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
            } // 若字段名不同可改为属性读取

            try
            {
                forceWeatherVal = (int)AccessTools.Field(wm.GetType(), "forceWeatherValue").GetValue(wm);
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

        // 3) 打包并发出
        var w = new NetDataWriter();
        w.Put((byte)Op.ENV_SYNC_STATE);
        w.Put(day);
        w.Put(secOfDay);
        w.Put(timeScale);
        w.Put(seed);
        w.Put(forceWeather);
        w.Put(forceWeatherVal);
        w.Put(currentWeather);
        w.Put(stormLevel);

        try
        {
            var all = Object.FindObjectsOfType<LootBoxLoader>(true);
            // 收集 (key, active)
            var tmp = new List<(int k, bool on)>(all.Length);
            foreach (var l in all)
            {
                if (!l || !l.gameObject) continue;
                var k = LootManager.Instance.ComputeLootKey(l.transform);
                var on = l.gameObject.activeSelf; // 已经由 RandomActive 决定
                tmp.Add((k, on));
            }

            w.Put(tmp.Count);
            for (var i = 0; i < tmp.Count; ++i)
            {
                w.Put(tmp[i].k);
                w.Put(tmp[i].on);
            }
        }
        catch
        {
            // 防守式：写一个 0，避免客户端读表时越界
            w.Put(0);
        }

        // Door

        var includeDoors = target != null;
        if (includeDoors)
        {
            var doors = Object.FindObjectsOfType<global::Door>(true);
            var tmp = new List<(int key, bool closed)>(doors.Length);

            foreach (var d in doors)
            {
                if (!d) continue;
                var k = 0;
                try
                {
                    k = (int)AccessTools.Field(typeof(global::Door), "doorClosedDataKeyCached").GetValue(d);
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
                } // 兜底：没取到就当作关闭

                tmp.Add((k, closed));
            }

            w.Put(tmp.Count);
            for (var i = 0; i < tmp.Count; ++i)
            {
                w.Put(tmp[i].key);
                w.Put(tmp[i].closed);
            }
        }
        else
        {
            w.Put(0); // 周期广播不带门清单
        }

        w.Put(COOPManager.destructible._deadDestructibleIds.Count);
        foreach (var id in COOPManager.destructible._deadDestructibleIds) w.Put(id);

        if (target != null) target.Send(w, DeliveryMethod.ReliableOrdered);
        else netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
    }


    // ========== 环境同步：客户端请求 ==========
    public void Client_RequestEnvSync()
    {
        if (IsServer || connectedPeer == null) return;
        var w = new NetDataWriter();
        w.Put((byte)Op.ENV_SYNC_REQUEST);
        connectedPeer.Send(w, DeliveryMethod.Sequenced);
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
                AccessTools.Field(inst.GetType(), "days")?.SetValue(inst, day);
                AccessTools.Field(inst.GetType(), "secondsOfDay")?.SetValue(inst, secOfDay);
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
                AccessTools.Field(wm.GetType(), "seed")?.SetValue(wm, seed); // 写 seed :contentReference[oaicite:11]{index=11}
                wm.GetType().GetMethod("SetupModules", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(wm, null); // 把 seed 带给 Storm/Precipitation :contentReference[oaicite:12]{index=12}
                AccessTools.Field(wm.GetType(), "_weatherDirty")?.SetValue(wm, true); // 标脏以便下帧重新取 GetWeather
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