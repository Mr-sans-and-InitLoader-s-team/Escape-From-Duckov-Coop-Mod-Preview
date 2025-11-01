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

namespace EscapeFromDuckovCoopMod;

public class ModBehaviour : Duckov.Modding.ModBehaviour
{
    public Harmony Harmony;

    public void OnEnable()
    {
        Harmony = new Harmony("DETF_COOP");
        Harmony.PatchAll();

        var go = new GameObject("COOP_MOD_1");
        DontDestroyOnLoad(go);

        go.AddComponent<NetService>();
        COOPManager.InitManager();
        go.AddComponent<ModBehaviourF>();
        Loader();
    }

    public void Loader()
    {
        CoopLocalization.Initialize();

        var go = new GameObject("COOP_MOD_");
        DontDestroyOnLoad(go);

        go.AddComponent<SteamP2PLoader>();
        go.AddComponent<AIRequest>();
        go.AddComponent<Send_ClientStatus>();
        go.AddComponent<HealthM>();
        go.AddComponent<LoaclPlayerManager>();
        go.AddComponent<SendLocalPlayerStatus>();
        go.AddComponent<Spectator>();
        go.AddComponent<DeadLootBox>();
        go.AddComponent<LootManager>();
        go.AddComponent<SceneNet>();
        go.AddComponent<ModUI>();
        CoopTool.Init();

        DeferredInit();
    }

    private void DeferredInit()
    {
        SafeInit<SteamP2PLoader>(s => s.Init());
        SafeInit<SceneNet>(sn => sn.Init());
        SafeInit<LootManager>(lm => lm.Init());
        SafeInit<LoaclPlayerManager>(lpm => lpm.Init());
        SafeInit<HealthM>(hm => hm.Init());
        SafeInit<SendLocalPlayerStatus>(s => s.Init());
        SafeInit<Spectator>(s => s.Init());
        SafeInit<ModUI>(ui => ui.Init());
        SafeInit<AIRequest>(a => a.Init());
        SafeInit<Send_ClientStatus>(s => s.Init());
        SafeInit<DeadLootBox>(s => s.Init());
       
    }

    private void SafeInit<T>(Action<T> init) where T : Component
    {
        var c = FindObjectOfType<T>();
        if (c == null) return;
        try
        {
            init(c);
        }
        catch
        {
        }
    }






}