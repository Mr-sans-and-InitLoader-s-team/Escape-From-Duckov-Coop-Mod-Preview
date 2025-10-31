// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using Steamworks;
using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.Steam
{
    public class SteamLobbyManager : MonoBehaviour
    {
        public static SteamLobbyManager Instance { get; private set; }
        
        private CSteamID currentLobbyId = CSteamID.Nil;
        public bool IsInLobby => currentLobbyId.IsValid();
        
        private Callback<LobbyCreated_t> _lobbyCreatedCallback;
        private Callback<LobbyEnter_t> _lobbyEnterCallback;
        private Callback<GameLobbyJoinRequested_t> _gameLobbyJoinRequestedCallback;
        
        public event System.Action<CSteamID> OnLobbyCreatedEvent;
        public event System.Action<CSteamID> OnLobbyJoinedEvent;
        
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeCallbacks();
        }
        
        private void InitializeCallbacks()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamLobbyManager] Steam未初始化");
                return;
            }
            
            _lobbyCreatedCallback = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _gameLobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            
            Debug.Log("[SteamLobbyManager] 回调已初始化");
        }
        
        public void CreateLobby(string lobbyName, string password, int maxMembers, ELobbyType lobbyType)
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamLobbyManager] Steam未初始化，无法创建大厅");
                return;
            }
            
            Debug.Log("[SteamLobbyManager] 创建大厅: " + lobbyName + " (类型: " + lobbyType + ", 最大人数: " + maxMembers + ")");
            SteamMatchmaking.CreateLobby(lobbyType, maxMembers);
        }
        
        public void JoinLobby(CSteamID lobbyId, string password = "")
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamLobbyManager] Steam未初始化，无法加入大厅");
                return;
            }
            
            Debug.Log("[SteamLobbyManager] 加入大厅: " + lobbyId);
            SteamMatchmaking.JoinLobby(lobbyId);
        }
        
        public void LeaveLobby()
        {
            if (!IsInLobby)
            {
                Debug.LogWarning("[SteamLobbyManager] 未在大厅中");
                return;
            }
            
            Debug.Log("[SteamLobbyManager] 离开大厅: " + currentLobbyId);
            SteamMatchmaking.LeaveLobby(currentLobbyId);
            currentLobbyId = CSteamID.Nil;
        }
        
        public CSteamID GetLobbyOwner()
        {
            if (!IsInLobby)
                return CSteamID.Nil;
            return SteamMatchmaking.GetLobbyOwner(currentLobbyId);
        }
        
        public CSteamID GetCurrentLobbyId()
        {
            return currentLobbyId;
        }
        
        public void InviteFriend()
        {
            if (!IsInLobby)
            {
                Debug.LogWarning("[SteamLobbyManager] 未在大厅中，无法邀请好友");
                return;
            }
            
            if (SteamUtils.IsOverlayEnabled())
            {
                SteamFriends.ActivateGameOverlayInviteDialog(currentLobbyId);
                Debug.Log("[SteamLobbyManager] 打开Steam邀请好友界面");
            }
            else
            {
                Debug.LogWarning("[SteamLobbyManager] Steam覆盖层未启用");
            }
        }
        
        private void OnLobbyCreated(LobbyCreated_t callback)
        {
            if (callback.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogError("[SteamLobbyManager] 创建大厅失败: " + callback.m_eResult);
                return;
            }
            
            currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            Debug.Log("[SteamLobbyManager] 大厅创建成功: " + currentLobbyId);
            
            SteamMatchmaking.SetLobbyData(currentLobbyId, "name", "Duckov Coop");
            SteamMatchmaking.SetLobbyData(currentLobbyId, "version", "1.0");
            SteamMatchmaking.SetLobbyData(currentLobbyId, "mod_id", "EscapeFromDuckovCoopMod_v1.0");
            
            Debug.Log("[SteamLobbyManager] 大厅主机，准备启动服务器（将在OnLobbyEnter时启动）");
            
            OnLobbyCreatedEvent?.Invoke(currentLobbyId);
        }
        
        private void OnLobbyEnter(LobbyEnter_t callback)
        {
            currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            
            EChatRoomEnterResponse response = (EChatRoomEnterResponse)callback.m_EChatRoomEnterResponse;
            
            if (response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Debug.LogError("[SteamLobbyManager] 加入大厅失败: " + response);
                currentLobbyId = CSteamID.Nil;
                return;
            }
            
            Debug.Log("[SteamLobbyManager] 成功加入大厅: " + currentLobbyId);
            
            CSteamID ownerId = SteamMatchmaking.GetLobbyOwner(currentLobbyId);
            bool amIOwner = ownerId == SteamUser.GetSteamID();
            
            Debug.Log("[SteamLobbyManager] 大厅主机: " + ownerId + " (我是主机: " + amIOwner + ")");
            
            OnLobbyJoinedEvent?.Invoke(currentLobbyId);
            
            if (!amIOwner)
            {
                Debug.Log("[SteamLobbyManager] 触发客户端连接流程");
                SteamLobbyHelper.TriggerMultiplayerConnect(ownerId);
            }
            else
            {
                Debug.Log("[SteamLobbyManager] 触发主机启动流程");
                SteamLobbyHelper.TriggerMultiplayerHost();
            }
        }
        
        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
        {
            Debug.Log("[SteamLobbyManager] 收到大厅邀请: " + callback.m_steamIDLobby);
            JoinLobby(callback.m_steamIDLobby);
        }
        
        private void OnDestroy()
        {
            if (IsInLobby)
            {
                LeaveLobby();
            }
        }
    }
}

