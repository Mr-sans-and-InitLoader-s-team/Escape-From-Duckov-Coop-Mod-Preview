using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace EscapeFromDuckovCoopMod
{
    public class SteamP2PLoader : MonoBehaviour
    {
        public static SteamP2PLoader Instance { get; private set; }
        public  bool UseSteamP2P = true;
        public  bool FallbackToUDP = true;
        public  bool _isOptimized = false;
        public void Init()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeMod();
        }

        private void InitializeMod()
        {
            try
            {
                Debug.Log("[SteamP2P扩展] 开始初始化...");
                if (!SteamManager.Initialized)
                {
                    Debug.LogWarning("[SteamP2P扩展] Steam未初始化，将使用UDP模式");
                    UseSteamP2P = false;
                    if (!FallbackToUDP)
                    {
                        Debug.LogError("[SteamP2P扩展] Steam不可用且禁用了UDP回退，MOD将不工作");
                        return;
                    }
                }
                if (UseSteamP2P && SteamManager.Initialized)
                {
                    gameObject.AddComponent<SteamP2PManager>();
                    gameObject.AddComponent<SteamEndPointMapper>();
                    gameObject.AddComponent<SteamLobbyManager>();
                    Debug.Log("[SteamP2P扩展] Steam P2P组件已启动");
                }
                Debug.Log("[SteamP2P扩展] 初始化完成！按F9使用Steam邀请好友（需先开始游戏）");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamP2P扩展] 初始化失败: {ex}");
            }
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (SteamLobbyManager.Instance != null)
                {
                    SteamLobbyManager.Instance.InviteFriend();
                    Debug.Log("[SteamP2P扩展] 打开Steam邀请界面（Shift+Tab也可以）");
                }
            }
            if (Input.GetKeyDown(KeyCode.F10))
            {
                if (SteamP2PManager.Instance != null)
                {
                    Debug.Log("╔════════════════════════════════════════════╗");
                    Debug.Log("║       Steam P2P 连接统计                   ║");
                    Debug.Log("╚════════════════════════════════════════════╝");
                    Debug.Log($"发送: {SteamP2PManager.Instance.PacketsSent} 包, {SteamP2PManager.Instance.BytesSent} bytes");
                    Debug.Log($"接收: {SteamP2PManager.Instance.PacketsReceived} 包, {SteamP2PManager.Instance.BytesReceived} bytes");
                    Debug.Log($"队列: {SteamP2PManager.Instance.GetQueueSize()} 个待处理数据包");
                    if (SteamEndPointMapper.Instance != null)
                    {
                        var steamIDs = SteamEndPointMapper.Instance.GetAllSteamIDs();
                        Debug.Log($"连接数: {steamIDs.Count}");
                        foreach (var steamID in steamIDs)
                        {
                            Debug.Log($"  - {steamID}");
                        }
                    }
                }
            }
        }










    }
}
