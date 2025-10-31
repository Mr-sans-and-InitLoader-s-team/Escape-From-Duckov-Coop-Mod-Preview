using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EscapeFromDuckovCoopMod
{
    public class SteamLobbyManager : MonoBehaviour
    {
        public readonly struct LobbyInfo
        {
            public LobbyInfo(CSteamID lobbyId, string lobbyName, string hostName, int memberCount, int maxMembers, bool requiresPassword)
            {
                LobbyId = lobbyId;
                LobbyName = lobbyName;
                HostName = hostName;
                MemberCount = memberCount;
                MaxMembers = maxMembers;
                RequiresPassword = requiresPassword;
            }

            public CSteamID LobbyId { get; }
            public string LobbyName { get; }
            public string HostName { get; }
            public int MemberCount { get; }
            public int MaxMembers { get; }
            public bool RequiresPassword { get; }
        }

        public enum LobbyJoinError
        {
            None,
            SteamNotInitialized,
            LobbyMetadataUnavailable,
            IncorrectPassword
        }

        private sealed class LobbyMetadata
        {
            public LobbyInfo Info;
            public string PasswordHash = string.Empty;
        }

        public static SteamLobbyManager Instance { get; private set; }

        private CSteamID _currentLobbyId = CSteamID.Nil;
        private bool _isHost;
        private SteamLobbyOptions _pendingLobbyOptions = SteamLobbyOptions.CreateDefault();

        private readonly Dictionary<CSteamID, LobbyMetadata> _lobbyMetadata = new();
        private readonly List<LobbyInfo> _availableLobbies = new();

        public IReadOnlyList<LobbyInfo> AvailableLobbies => _availableLobbies;

        public event Action<IReadOnlyList<LobbyInfo>>? LobbyListUpdated;

        public bool IsInLobby => _currentLobbyId != CSteamID.Nil;
        public bool IsHost => _isHost;
        public CSteamID CurrentLobbyId => _currentLobbyId;

        private CallResult<LobbyCreated_t> _lobbyCreatedCallback = null!;
        private Callback<LobbyEnter_t> _lobbyEnterCallback = null!;
        private Callback<LobbyChatUpdate_t> _lobbyChatUpdateCallback = null!;
        private CallResult<LobbyMatchList_t> _lobbyMatchListCallback = null!;
        private Callback<GameLobbyJoinRequested_t> _gameLobbyJoinRequestedCallback = null!;
        private Callback<LobbyDataUpdate_t> _lobbyDataUpdateCallback = null!;

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
                Debug.LogError("[SteamLobby] Steam未初始化");
                return;
            }

            _lobbyCreatedCallback = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _lobbyMatchListCallback = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
            _gameLobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            _lobbyDataUpdateCallback = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);

            Debug.Log("[SteamLobby] 回调已设置（包括邀请监听和数据更新）");
        }

        public void CreateLobby(SteamLobbyOptions? options = null)
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamLobby] Steam未初始化");
                return;
            }

            _pendingLobbyOptions = options ?? SteamLobbyOptions.CreateDefault();
            _pendingLobbyOptions.MaxPlayers = Mathf.Clamp(_pendingLobbyOptions.MaxPlayers, 2, 16);

            var lobbyType = _pendingLobbyOptions.Visibility == SteamLobbyVisibility.Public
                ? ELobbyType.k_ELobbyTypePublic
                : ELobbyType.k_ELobbyTypeFriendsOnly;

            Debug.Log($"[SteamLobby] 创建Lobby，最大玩家数: {_pendingLobbyOptions.MaxPlayers}，可见性: {_pendingLobbyOptions.Visibility}");

            SteamAPICall_t apiCall = SteamMatchmaking.CreateLobby(
                lobbyType,
                _pendingLobbyOptions.MaxPlayers
            );
            _lobbyCreatedCallback.Set(apiCall);
        }

        public void RequestLobbyList()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogWarning("[SteamLobby] 无法请求Lobby列表：Steam未初始化");
                return;
            }

            Debug.Log("[SteamLobby] 请求当前游戏的Lobby列表");
            _lobbyMetadata.Clear();
            _availableLobbies.Clear();
            LobbyListUpdated?.Invoke(_availableLobbies);

            SteamMatchmaking.AddRequestLobbyListStringFilter("game", "duckov_multiplayer", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.RequestLobbyList();
        }

        public bool TryGetLobbyInfo(CSteamID lobbyId, out LobbyInfo info)
        {
            if (_lobbyMetadata.TryGetValue(lobbyId, out var meta))
            {
                info = meta.Info;
                return true;
            }

            info = default;
            return false;
        }

        public bool TryJoinLobbyWithPassword(CSteamID lobbyId, string password, out LobbyJoinError error)
        {
            error = LobbyJoinError.None;

            if (!SteamManager.Initialized)
            {
                error = LobbyJoinError.SteamNotInitialized;
                return false;
            }

            if (!_lobbyMetadata.TryGetValue(lobbyId, out var meta))
            {
                error = LobbyJoinError.LobbyMetadataUnavailable;
                SteamMatchmaking.RequestLobbyData(lobbyId);
                return false;
            }

            if (!string.IsNullOrEmpty(meta.PasswordHash))
            {
                var providedHash = HashPassword(password);
                if (!string.Equals(providedHash, meta.PasswordHash, StringComparison.Ordinal))
                {
                    error = LobbyJoinError.IncorrectPassword;
                    return false;
                }
            }

            JoinLobby(lobbyId);
            return true;
        }

        public void JoinLobby(CSteamID lobbyId)
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamLobby] Steam未初始化");
                return;
            }

            Debug.Log($"[SteamLobby] 加入Lobby: {lobbyId}");
            SteamMatchmaking.JoinLobby(lobbyId);
        }

        public void LeaveLobby()
        {
            if (_currentLobbyId != CSteamID.Nil)
            {
                Debug.Log($"[SteamLobby] 离开Lobby: {_currentLobbyId}");
                SteamMatchmaking.LeaveLobby(_currentLobbyId);
                _currentLobbyId = CSteamID.Nil;
                _isHost = false;
            }
        }

        public void InviteFriend()
        {
            if (_currentLobbyId == CSteamID.Nil)
            {
                Debug.LogWarning("[SteamLobby] 当前不在Lobby中");
                return;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(_currentLobbyId);
            Debug.Log("[SteamLobby] 打开Steam邀请对话框");
        }

        public CSteamID GetLobbyOwner()
        {
            if (_currentLobbyId == CSteamID.Nil)
            {
                return CSteamID.Nil;
            }

            return SteamMatchmaking.GetLobbyOwner(_currentLobbyId);
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
        {
            Debug.Log("╔════════════════════════════════════════════╗");
            Debug.Log("║   Steam邀请：玩家点击了加入游戏！          ║");
            Debug.Log("╚════════════════════════════════════════════╝");
            CSteamID lobbyId = callback.m_steamIDLobby;
            CSteamID friendId = callback.m_steamIDFriend;
            Debug.Log("[SteamLobby] 收到加入Lobby邀请");
            Debug.Log($"[SteamLobby] Lobby ID: {lobbyId}");
            Debug.Log($"[SteamLobby] 来自好友: {friendId} ({SteamFriends.GetFriendPersonaName(friendId)})");
            JoinLobby(lobbyId);
        }

        private void OnLobbyCreated(LobbyCreated_t callback, bool bIOFailure)
        {
            if (bIOFailure || callback.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogError($"[SteamLobby] 创建Lobby失败: {callback.m_eResult}");
                return;
            }

            _currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            _isHost = true;

            Debug.Log($"[SteamLobby] Lobby创建成功: {_currentLobbyId}");
            Debug.Log("[SteamLobby] ✓ 设置为主机模式");

            ApplyLobbyMetadata(_pendingLobbyOptions);

            Debug.Log("[SteamLobby] Lobby创建成功，自动启动联机服务器");
            SteamLobbyHelper.TriggerMultiplayerHost();
        }

        private void ApplyLobbyMetadata(SteamLobbyOptions options)
        {
            var lobbyName = string.IsNullOrWhiteSpace(options.LobbyName)
                ? "Duckov Lobby"
                : options.LobbyName.Trim();

            SteamMatchmaking.SetLobbyMemberLimit(_currentLobbyId, options.MaxPlayers);
            SteamMatchmaking.SetLobbyData(_currentLobbyId, "game", "duckov_multiplayer");
            SteamMatchmaking.SetLobbyData(_currentLobbyId, "version", "1.0.0");
            SteamMatchmaking.SetLobbyData(_currentLobbyId, "name", lobbyName);

            var hostName = SteamFriends.GetPersonaName();
            if (string.IsNullOrWhiteSpace(hostName))
            {
                hostName = "Host";
            }
            SteamMatchmaking.SetLobbyData(_currentLobbyId, "host", hostName);

            var passwordHash = HashPassword(options.Password);
            SteamMatchmaking.SetLobbyData(_currentLobbyId, "password", passwordHash);
            SteamMatchmaking.SetLobbyData(_currentLobbyId, "password_protected", string.IsNullOrEmpty(passwordHash) ? "0" : "1");
            SteamMatchmaking.SetLobbyJoinable(_currentLobbyId, true);

            var metadata = new LobbyMetadata
            {
                Info = new LobbyInfo(
                    _currentLobbyId,
                    lobbyName,
                    hostName,
                    SteamMatchmaking.GetNumLobbyMembers(_currentLobbyId),
                    SteamMatchmaking.GetLobbyMemberLimit(_currentLobbyId),
                    !string.IsNullOrEmpty(passwordHash)
                ),
                PasswordHash = passwordHash
            };

            _lobbyMetadata[_currentLobbyId] = metadata;
            RefreshLobbyListCache();
        }

        private void OnLobbyEnter(LobbyEnter_t callback)
        {
            _currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);

            if (callback.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Debug.Log($"[SteamLobby] 成功加入Lobby: {_currentLobbyId}");
                CSteamID hostId = SteamMatchmaking.GetLobbyOwner(_currentLobbyId);
                CSteamID myId = SteamUser.GetSteamID();
                Debug.Log($"[SteamLobby] Lobby主机: {hostId}");
                Debug.Log($"[SteamLobby] 我的Steam ID: {myId}");

                if (hostId == myId)
                {
                    Debug.Log("[SteamLobby] ✓ 我是主机，无需连接自己");
                    _isHost = true;
                    return;
                }

                Debug.Log("[SteamLobby] ✓ 我是客户端，准备连接到主机");
                _isHost = false;

                if (SteamEndPointMapper.Instance != null)
                {
                    var hostEndPoint = SteamEndPointMapper.Instance.RegisterSteamID(hostId, 27015);
                    Debug.Log($"[SteamLobby] 主机映射到: {hostEndPoint}");
                }

                Debug.Log("[SteamLobby] Lobby加入成功，自动连接到主机");
                SteamLobbyHelper.TriggerMultiplayerConnect(hostId);
            }
            else
            {
                Debug.LogError($"[SteamLobby] 加入Lobby失败: {callback.m_EChatRoomEnterResponse}");
            }
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t callback)
        {
            CSteamID lobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            CSteamID userId = new CSteamID(callback.m_ulSteamIDUserChanged);
            EChatMemberStateChange stateChange = (EChatMemberStateChange)callback.m_rgfChatMemberStateChange;
            string userName = SteamFriends.GetFriendPersonaName(userId);

            switch (stateChange)
            {
                case EChatMemberStateChange.k_EChatMemberStateChangeEntered:
                    Debug.Log($"[SteamLobby] {userName} 加入了Lobby");
                    if (SteamEndPointMapper.Instance != null)
                    {
                        SteamEndPointMapper.Instance.RegisterSteamID(userId);
                    }
                    break;
                case EChatMemberStateChange.k_EChatMemberStateChangeLeft:
                    Debug.Log($"[SteamLobby] {userName} 离开了Lobby");
                    if (SteamEndPointMapper.Instance != null)
                    {
                        SteamEndPointMapper.Instance.UnregisterSteamID(userId);
                    }
                    break;
                case EChatMemberStateChange.k_EChatMemberStateChangeDisconnected:
                    Debug.Log($"[SteamLobby] {userName} 断开连接");
                    break;
                case EChatMemberStateChange.k_EChatMemberStateChangeKicked:
                    Debug.Log($"[SteamLobby] {userName} 被踢出");
                    break;
                case EChatMemberStateChange.k_EChatMemberStateChangeBanned:
                    Debug.Log($"[SteamLobby] {userName} 被封禁");
                    break;
            }

            if (_lobbyMetadata.TryGetValue(lobbyId, out var meta))
            {
                meta.Info = new LobbyInfo(
                    meta.Info.LobbyId,
                    meta.Info.LobbyName,
                    meta.Info.HostName,
                    SteamMatchmaking.GetNumLobbyMembers(lobbyId),
                    SteamMatchmaking.GetLobbyMemberLimit(lobbyId),
                    meta.Info.RequiresPassword
                );
                _lobbyMetadata[lobbyId] = meta;
                RefreshLobbyListCache();
            }
        }

        private void OnLobbyMatchList(LobbyMatchList_t callback, bool bIOFailure)
        {
            if (bIOFailure)
            {
                Debug.LogError("[SteamLobby] 获取Lobby列表失败");
                return;
            }

            Debug.Log($"[SteamLobby] 找到 {callback.m_nLobbiesMatching} 个Lobby");

            for (int i = 0; i < callback.m_nLobbiesMatching; i++)
            {
                var lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
                SteamMatchmaking.RequestLobbyData(lobbyId);
            }
        }

        private void OnLobbyDataUpdate(LobbyDataUpdate_t callback)
        {
            if (callback.m_bSuccess == 0)
            {
                return;
            }

            var lobbyId = new CSteamID(callback.m_ulSteamIDLobby);

            // Only handle lobby-wide data updates
            if (callback.m_ulSteamIDMember != callback.m_ulSteamIDLobby)
            {
                return;
            }

            var lobbyName = SteamMatchmaking.GetLobbyData(lobbyId, "name");
            if (string.IsNullOrWhiteSpace(lobbyName))
            {
                lobbyName = $"Lobby {lobbyId}";
            }

            var hostName = SteamMatchmaking.GetLobbyData(lobbyId, "host");
            if (string.IsNullOrWhiteSpace(hostName))
            {
                hostName = "Host";
            }

            var passwordHash = SteamMatchmaking.GetLobbyData(lobbyId, "password");
            var requiresPassword = !string.IsNullOrEmpty(passwordHash);
            var memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            var maxMembers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);

            _lobbyMetadata[lobbyId] = new LobbyMetadata
            {
                Info = new LobbyInfo(lobbyId, lobbyName, hostName, memberCount, maxMembers, requiresPassword),
                PasswordHash = passwordHash
            };

            RefreshLobbyListCache();
        }

        private void RefreshLobbyListCache()
        {
            _availableLobbies.Clear();
            _availableLobbies.AddRange(_lobbyMetadata.Values.Select(meta => meta.Info).OrderBy(info => info.LobbyName, StringComparer.OrdinalIgnoreCase));
            LobbyListUpdated?.Invoke(_availableLobbies);
        }

        private static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hashBytes = sha.ComputeHash(bytes);

                // 手工把 byte[] 转十六进制（大写）
                var sb = new StringBuilder(hashBytes.Length * 2);
                for (int i = 0; i < hashBytes.Length; i++)
                    sb.Append(hashBytes[i].ToString("X2"));
                return sb.ToString();
            }
        }

        private void OnDestroy()
        {
            LeaveLobby();
            LobbyListUpdated = null;
            _availableLobbies.Clear();
            _lobbyMetadata.Clear();
        }
    }
}
