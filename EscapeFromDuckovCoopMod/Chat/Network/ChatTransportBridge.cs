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
using UnityEngine;
using Steamworks;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 聊天传输桥接器
    /// 连接旧的网络系统和新的统一聊天传输层
    /// </summary>
    public static class ChatTransportBridge
    {
        /// <summary>
        /// 初始化聊天传输层
        /// </summary>
        /// <param name="isServer">是否为主机</param>
        /// <param name="useSteamP2P">是否使用 Steam P2P（仅客机端使用，主机端忽略此参数）</param>
        public static void InitializeTransport(bool isServer, bool useSteamP2P)
        {
            try
            {
                Debug.Log($"[ChatTransportBridge] 初始化聊天传输层: IsServer={isServer}, UseSteamP2P={useSteamP2P}");

                var transport = UnifiedChatTransport.Instance;

                // 设置传输模式
                if (isServer)
                {
                    // 主机模式：始终支持双模（Steam P2P + 直连 UDP 9050）
                    // 主机不管 TransportMode 开关，同时建立 P2P 和直连监听
                    CSteamID lobbyId = GetCurrentLobbyId(true); // 主机始终尝试获取大厅 ID
                    transport.SetAsHost(lobbyId);
                    Debug.Log($"[ChatTransportBridge] ✓ 设置为主机模式（双模支持）");
                    Debug.Log($"[ChatTransportBridge] ✓ Steam 大厅 ID: {(lobbyId.IsValid() ? lobbyId.ToString() : "无（仅直连 UDP）")}");
                    Debug.Log($"[ChatTransportBridge] ✓ 主机将同时监听 Steam P2P 和直连 UDP 9050");
                }
                else
                {
                    // 客机模式：根据 TransportMode 决定是直连还是 P2P
                    // 但仍然尝试获取大厅 ID，以便在 P2P 模式下使用
                    CSteamID lobbyId = GetCurrentLobbyId(useSteamP2P); // 只有在 P2P 模式下才获取大厅 ID
                    transport.SetAsClient(lobbyId);
                    
                    if (useSteamP2P && lobbyId.IsValid())
                    {
                        Debug.Log($"[ChatTransportBridge] ✓ 设置为客户端模式（Steam P2P）");
                        Debug.Log($"[ChatTransportBridge] ✓ Steam 大厅 ID: {lobbyId}");
                        
                        // 立即从大厅获取主机的 SteamID 并注册到聊天传输层
                        CSteamID hostSteamId = GetLobbyOwner(lobbyId);
                        if (hostSteamId.IsValid())
                        {
                            // 注册主机的 SteamID（使用虚拟端点格式）
                            string hostEndpoint = $"10.255.0.1:27015"; // 主机的虚拟端点
                            transport.RegisterClientSteamId(hostEndpoint, hostSteamId);
                            Debug.Log($"[ChatTransportBridge] ✓ 已注册主机 SteamID: {hostEndpoint} <-> {hostSteamId}");
                        }
                        else
                        {
                            Debug.LogWarning($"[ChatTransportBridge] ⚠ 无法获取大厅主机的 SteamID");
                        }
                    }
                    else if (useSteamP2P && !lobbyId.IsValid())
                    {
                        Debug.LogWarning($"[ChatTransportBridge] ⚠ 客户端请求 Steam P2P 但未找到大厅，将回退到直连 UDP");
                    }
                    else
                    {
                        Debug.Log($"[ChatTransportBridge] ✓ 设置为客户端模式（直连 UDP）");
                    }
                }

                // 订阅传输层事件
                transport.OnChatMessageReceived -= OnTransportMessageReceived;
                transport.OnChatMessageReceived += OnTransportMessageReceived;

                Debug.Log($"[ChatTransportBridge] ✓ 初始化完成，状态: {transport.GetTransportStatus()}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatTransportBridge] ✗ 初始化时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送聊天消息（供 Mod.cs 调用）
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="targetEndpoint">目标端点（null 表示广播）</param>
        /// <returns>发送是否成功</returns>
        public static bool SendChatMessage(string messageJson, string targetEndpoint = null)
        {
            try
            {
                var transport = UnifiedChatTransport.Instance;
                return transport.SendChatMessage(messageJson, targetEndpoint);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatTransportBridge] 发送消息时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 注册客户端的 SteamID 映射（供网络系统调用）
        /// </summary>
        /// <param name="endpoint">客户端端点</param>
        /// <param name="steamId">Steam ID</param>
        public static void RegisterClientSteamId(string endpoint, CSteamID steamId)
        {
            try
            {
                var transport = UnifiedChatTransport.Instance;
                transport.RegisterClientSteamId(endpoint, steamId);
                Debug.Log($"[ChatTransportBridge] 注册客户端 SteamID: {endpoint} <-> {steamId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatTransportBridge] 注册客户端 SteamID 时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理直连 UDP 聊天消息（供 NetService 调用）
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="senderEndpoint">发送者端点</param>
        public static void HandleDirectUDPMessage(string messageJson, string senderEndpoint)
        {
            try
            {
                var transport = UnifiedChatTransport.Instance;
                transport.HandleDirectUDPChatMessage(messageJson, senderEndpoint);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatTransportBridge] 处理直连 UDP 消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取传输状态
        /// </summary>
        /// <returns>状态描述</returns>
        public static string GetTransportStatus()
        {
            try
            {
                var transport = UnifiedChatTransport.Instance;
                return transport.GetTransportStatus();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatTransportBridge] 获取传输状态时发生异常: {ex.Message}");
                return "错误";
            }
        }

        #region 私有方法

        /// <summary>
        /// 获取当前 Steam 大厅 ID
        /// </summary>
        /// <param name="tryGetLobby">是否尝试获取大厅 ID</param>
        /// <returns>大厅 ID</returns>
        private static CSteamID GetCurrentLobbyId(bool tryGetLobby)
        {
            if (!tryGetLobby)
            {
                Debug.Log($"[ChatTransportBridge] 跳过获取大厅 ID");
                return default;
            }

            try
            {
                Debug.Log($"[ChatTransportBridge] 尝试获取 Steam 大厅 ID...");
                
                // 检查 Steam 是否初始化
                if (!SteamManager.Initialized)
                {
                    Debug.LogWarning($"[ChatTransportBridge] Steam 未初始化");
                    return default;
                }

                Debug.Log($"[ChatTransportBridge] SteamLobbyManager.Instance = {(SteamLobbyManager.Instance != null ? "存在" : "null")}");
                
                if (SteamLobbyManager.Instance != null)
                {
                    Debug.Log($"[ChatTransportBridge] IsInLobby = {SteamLobbyManager.Instance.IsInLobby}");
                    
                    if (SteamLobbyManager.Instance.IsInLobby)
                    {
                        var lobbyId = SteamLobbyManager.Instance.CurrentLobbyId;
                        Debug.Log($"[ChatTransportBridge] ✓ 获取到大厅 ID: {lobbyId}");
                        return lobbyId;
                    }
                    else
                    {
                        Debug.LogWarning($"[ChatTransportBridge] 当前不在大厅中");
                    }
                }
                else
                {
                    Debug.LogWarning($"[ChatTransportBridge] SteamLobbyManager 未初始化");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatTransportBridge] 获取大厅 ID 时发生异常: {ex.Message}");
                Debug.LogError($"[ChatTransportBridge] 异常堆栈: {ex.StackTrace}");
            }

            Debug.LogWarning($"[ChatTransportBridge] 返回默认大厅 ID（将使用直连 UDP）");
            return default;
        }

        /// <summary>
        /// 获取大厅的主机 SteamID
        /// </summary>
        /// <param name="lobbyId">大厅 ID</param>
        /// <returns>主机 SteamID</returns>
        private static CSteamID GetLobbyOwner(CSteamID lobbyId)
        {
            try
            {
                if (!lobbyId.IsValid())
                {
                    Debug.LogWarning($"[ChatTransportBridge] 无效的大厅 ID");
                    return default;
                }

                // 获取大厅所有者
                CSteamID ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
                
                if (ownerId.IsValid())
                {
                    Debug.Log($"[ChatTransportBridge] ✓ 获取到大厅主机 SteamID: {ownerId}");
                    return ownerId;
                }
                else
                {
                    Debug.LogWarning($"[ChatTransportBridge] 大厅主机 SteamID 无效");
                    return default;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatTransportBridge] 获取大厅主机时发生异常: {ex.Message}");
                return default;
            }
        }

        /// <summary>
        /// 处理传输层接收到的消息
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="senderEndpoint">发送者端点</param>
        private static void OnTransportMessageReceived(string messageJson, string senderEndpoint)
        {
            try
            {
                Debug.Log($"[ChatTransportBridge] 收到消息: {senderEndpoint} -> {messageJson}");

                // 判断是主机还是客户端
                bool isServer = NetService.Instance?.IsServer ?? false;

                if (isServer)
                {
                    // 主机收到消息，需要广播给其他客户端
                    Debug.Log($"[ChatTransportBridge] 主机收到消息，调用 HandleUDPChatMessage");
                    ModBehaviourF.Instance?.HandleUDPChatMessage(messageJson, senderEndpoint);
                }
                else
                {
                    // 客户端收到消息，直接显示
                    Debug.Log($"[ChatTransportBridge] 客户端收到消息，通知 ChatManager");
                    var chatManager = EscapeFromDuckovCoopMod.Chat.Managers.ChatManager.Instance;
                    if (chatManager != null)
                    {
                        chatManager.HandleNetworkMessage(messageJson);
                    }
                    else
                    {
                        Debug.LogWarning($"[ChatTransportBridge] ChatManager 未初始化");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatTransportBridge] 处理接收消息时发生异常: {ex.Message}");
            }
        }

        #endregion
    }
}
