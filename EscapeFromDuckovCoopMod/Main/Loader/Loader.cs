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
        Debug.Log("========================================");
        Debug.Log($"[EscapeFromDuckovCoopMod] Version {BuildInfo.ModVersion} Loading...");
        Debug.Log($"[EscapeFromDuckovCoopMod] Git Commit: {BuildInfo.GitCommit}");
        Debug.Log("========================================");
        
        Harmony = new Harmony("DETF_COOP");
        Harmony.PatchAll();

        var go = new GameObject("COOP_MOD_1");
        DontDestroyOnLoad(go);

        go.AddComponent<NetService>();
        COOPManager.InitManager();
        go.AddComponent<ModBehaviourF>();
        Loader();
        
        Debug.Log("[EscapeFromDuckovCoopMod] All systems loaded successfully!");
    }

    public void Loader()
    {
        Debug.Log("[Loader] Starting component initialization...");
        
        CoopLocalization.Initialize();
        Debug.Log("[Loader] Localization initialized");

        var go = new GameObject("COOP_MOD_");
        DontDestroyOnLoad(go);

        go.AddComponent<SteamP2PLoader>();
        go.AddComponent<EscapeFromDuckovCoopMod.Net.HybridP2P.HybridP2PRelay>();
        go.AddComponent<AIRequest>();
        go.AddComponent<Send_ClientStatus>();
        go.AddComponent<HealthM>();
        go.AddComponent<LocalPlayerManager>();
        go.AddComponent<SendLocalPlayerStatus>();
        go.AddComponent<Spectator>();
        
        Debug.Log("[Loader] Initializing teleport and player color systems...");
        go.AddComponent<TeleportManager>();
        go.AddComponent<PlayerColorManager>();
        Debug.Log("[Loader] Teleport and player color systems added");
        
        go.AddComponent<DeadLootBox>();
        go.AddComponent<LootManager>();
        go.AddComponent<SceneNet>();
        go.AddComponent<VoteSystemRPC>();
        go.AddComponent<MModUI>();
        CoopTool.Init();

        Debug.Log("[Loader] All components initialized, starting deferred initialization...");
        DeferredInit();
        Debug.Log("[Loader] Initialization complete!");
    }

    private void DeferredInit()
    {
        SafeInit<SteamP2PLoader>(s => s.Init());
        SafeInit<SceneNet>(sn => sn.Init());
        SafeInit<LootManager>(lm => lm.Init());
        SafeInit<LocalPlayerManager>(lpm => lpm.Init());
        SafeInit<HealthM>(hm => hm.Init());
        SafeInit<SendLocalPlayerStatus>(s => s.Init());
        SafeInit<Spectator>(s => s.Init());
        SafeInit<MModUI>(ui => ui.Init());
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