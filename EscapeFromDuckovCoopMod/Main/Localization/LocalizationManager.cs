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
using System.IO;
using UnityEngine;
using SodaCraft.Localizations;
using Newtonsoft.Json.Linq;

namespace EscapeFromDuckovCoopMod
{
    /// <summary>
    /// 중앙 집중식 로컬라이제이션 관리자
    /// JSON 파일에서 번역을 로드하고 관리합니다
    /// </summary>
    public static class CoopLocalization
    {
        private static Dictionary<string, string> currentTranslations = new Dictionary<string, string>();
        private static string currentLanguageCode = "en-US";
        private static bool isInitialized = false;
        private static SystemLanguage lastSystemLanguage = SystemLanguage.Unknown;

        /// <summary>
        /// 로컬라이제이션 시스템 초기화
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            // 게임의 현재 언어 감지
            DetectAndLoadLanguage();
            isInitialized = true;

            Debug.Log($"[CoopLocalization] Initialized with language: {currentLanguageCode}");
        }

        /// <summary>
        /// 시스템 언어 변경 확인 및 리로드
        /// </summary>
        public static void CheckLanguageChange()
        {
            if (!isInitialized) return;

            var currentSystemLang = LocalizationManager.CurrentLanguage;
            if (currentSystemLang != lastSystemLanguage)
            {
                Debug.Log($"[CoopLocalization] Language changed from {lastSystemLanguage} to {currentSystemLang}, reloading translations...");
                DetectAndLoadLanguage();
            }
        }

        /// <summary>
        /// 시스템 언어 감지 및 번역 로드
        /// </summary>
        private static void DetectAndLoadLanguage()
        {
            var systemLang = LocalizationManager.CurrentLanguage;
            lastSystemLanguage = systemLang;

            switch (systemLang)
            {
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional:
                case SystemLanguage.Chinese:
                    currentLanguageCode = "zh-CN";
                    break;
                case SystemLanguage.Korean:
                    currentLanguageCode = "ko-KR";
                    break;
                case SystemLanguage.Japanese:
                    currentLanguageCode = "ja-JP";
                    break;
                case SystemLanguage.Russian:
                    currentLanguageCode = "ru-RU";
                    break;
                case SystemLanguage.German:
                    currentLanguageCode = "de-DE";
                    break;
                case SystemLanguage.Portuguese:
                    currentLanguageCode = "pt-BR";
                    break;
                case SystemLanguage.English:
                default:
                    currentLanguageCode = "en-US";
                    break;
            }

            LoadTranslations(currentLanguageCode);
        }

        /// <summary>
        /// JSON 파일에서 번역 로드
        /// </summary>
        private static void LoadTranslations(string languageCode)
        {
            currentTranslations.Clear();

            try
            {
                // Mod 폴더 경로 찾기
                string modPath = Path.GetDirectoryName(typeof(CoopLocalization).Assembly.Location);
                string localizationPath = Path.Combine(modPath, "Localization", $"{languageCode}.json");

                // JSON 파일이 없으면 폴백으로 영어 사용
                if (!File.Exists(localizationPath))
                {
                    Debug.LogWarning($"[CoopLocalization] Translation file not found: {localizationPath}, using fallback translations");
                    LoadFallbackTranslations();
                    return;
                }

                string json = File.ReadAllText(localizationPath);

                // JSON 문자열의 이스케이프와 줄바꿈을 함께 처리
                ParseJsonTranslations(json);

                if (currentTranslations.Count > 0)
                {
                    Debug.Log($"[CoopLocalization] Loaded {currentTranslations.Count} translations from {localizationPath}");
                }
                else
                {
                    Debug.LogWarning($"[CoopLocalization] Failed to parse translation file, using fallback");
                    LoadFallbackTranslations();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[CoopLocalization] Error loading translations: {e.Message}");
                LoadFallbackTranslations();
            }
        }

        private static void ParseJsonTranslations(string json)
        {
            var entries = JObject.Parse(json)["translations"] as JArray;
            if (entries == null) return;
            foreach (var entry in entries)
            {
                var key = (string)entry["key"];
                var value = (string)entry["value"];
                if (!string.IsNullOrEmpty(key) && value != null)
                    currentTranslations[key] = value;
            }
        }

        /// <summary>
        /// 폴백 번역 로드 (JSON 파일이 없을 때)
        /// </summary>
        private static void LoadFallbackTranslations()
        {
            // 기본 영어 번역을 하드코딩으로 제공
            currentTranslations.Clear();
            currentTranslations["ui.window.title"] = "Co-op Mod Control Panel";
            currentTranslations["ui.sharedHome.displayHostOnly"] = "In shared mode, the host edits display contents and guests see the synchronized display. All players can use the common warehouse.";
            currentTranslations["net.sharedHome.entryFailed"] = "Could not load the shared room. Disconnected and restored your personal save. Please check the log.";
            currentTranslations["net.sharedHome.title"] = "Shared mode: connection failed";
            currentTranslations["net.sharedHome.close"] = "Close";
            currentTranslations["net.sharedHome.rejectedHost"] = "Player {0} could not connect: shared mode compatibility check failed.";
            currentTranslations["net.sharedHome.rejectedClient"] = "Could not join: shared mode compatibility check failed.";
            currentTranslations["net.sharedHome.invalid"] = "The enabled mod list or game version could not be verified. Restart both games and check that all enabled mods load correctly.";
            currentTranslations["net.sharedHome.protocol"] = "Shared mode validation protocols differ. Install the same co-op build on both sides.";
            currentTranslations["net.sharedHome.game"] = "Game version differs. Host: {1}; client: {2}.";
            currentTranslations["net.sharedHome.coop"] = "Co-op version differs. Host: {1}; client: {2}.";
            currentTranslations["net.sharedHome.missing"] = "Client is missing enabled mod: {0} (host version: {1}).";
            currentTranslations["net.sharedHome.extra"] = "Client has an extra enabled mod: {0} (client version: {2}).";
            currentTranslations["net.sharedHome.version"] = "Mod version differs: {0}. Host: {1}; client: {2}.";
            currentTranslations["net.sharedHome.hash"] = "Mod DLL differs: {0}.\nHost SHA-256:\n{1}\nClient SHA-256:\n{2}";
            currentTranslations["net.sharedHome.unreadable"] = "Could not read the DLL for enabled mod: {0}. Restore the mod files and restart the game.";
            currentTranslations["net.sharedHome.timeout"] = "Shared mode compatibility validation timed out. Check the connection and install the same co-op build on both sides.";
            currentTranslations["ui.sharedHome"] = "Private shared mode";
            currentTranslations["ui.sharedHome.desc"] = "Enable before hosting; guests share automatically. The host saves experience, levels, skills, quests, kills, maps, beacons, recipes, money, storage, buildings and decorations. Appearance, backpacks and equipment are saved per player and restored on rejoining. First visits create a character with native customization; tutorial progress follows the host. Personal saves are preserved and restored on leaving. Locked while connected.";
            currentTranslations["ui.coopMode.title"] = "Choose your co-op mode";
            currentTranslations["ui.coopMode.hint"] = "Hover over a card for details. When joining a room, the host decides the mode.";
            currentTranslations["ui.coopMode.select"] = "SELECT  ›";
            currentTranslations["ui.coopMode.independent.title"] = "Independent Co-op";
            currentTranslations["ui.coopMode.independent.tag"] = "YOUR CHARACTER · YOUR PROGRESS";
            currentTranslations["ui.coopMode.independent.summary"] = "• Explore and fight together<br>• Keep your own character progress<br>• Separate personal storage";
            currentTranslations["ui.coopMode.independent.detail"] = "Your own journey<br>Keep your personal experience, skills, quests and unlocks.<br><br>Your own equipment<br>Use your own storage, backpack and worn gear.<br><br>Team up to explore, fight and extract together.";
            currentTranslations["ui.coopMode.shared.title"] = "Shared Co-op";
            currentTranslations["ui.coopMode.shared.tag"] = "ONE WORLD · SHARED PROGRESS";
            currentTranslations["ui.coopMode.shared.summary"] = "• Build one world together<br>• Shared progress and public storage<br>• Characters saved by the host";
            currentTranslations["ui.coopMode.shared.detail"] = "One shared adventure<br>Experience, levels, skills, quests, map unlocks, storage, buildings and decorations follow the host world.<br><br>Your own character<br>The host saves your appearance, backpack and gear. New visitors create a character.<br><br>Your personal save stays separate.";
            currentTranslations["ui.steam.coopMode.shared"] = "Shared co-op";
            currentTranslations["ui.steam.coopMode.independent"] = "Independent co-op";
            currentTranslations["ui.steam.coopMode.unknown"] = "Mode unknown";
            currentTranslations["ui.coopMode.switch"] = "Switch mode";
            currentTranslations["net.disconnect.returning"] = "Returning to the main menu. Rejoin to return to the host map.";
            currentTranslations["net.disconnect.reconnect"] = "Check your network and reconnect to the host.";
            currentTranslations["net.reconnect.cancel"] = "Cancel reconnect";
            currentTranslations["net.reconnect.countdown"] = "Reconnecting in {0} seconds";
            currentTranslations["net.reconnect.connecting"] = "Reconnecting ({0}/5)…";
            currentTranslations["net.reconnect.connected"] = "Connection restored.";
            currentTranslations["net.reconnect.stopped"] = "Automatic reconnect stopped. You can rejoin manually.";
            currentTranslations["ui.coopMode.eyebrow"] = "ESCAPE FROM DUCKOV  /  COOPERATIVE";
            currentTranslations["ui.window.playerStatus"] = "Player Status";
            currentTranslations["ui.mode.current"] = "Current Mode";
            currentTranslations["ui.mode.server"] = "Server";
            currentTranslations["ui.mode.client"] = "Client";
            currentTranslations["ui.mode.switchTo"] = "Switch to {0} Mode";
            currentTranslations["ui.hostList.title"] = "🔍 LAN Host List";
            currentTranslations["ui.hostList.empty"] = "(Waiting for broadcast, no hosts found)";
            currentTranslations["ui.hostList.connect"] = "Connect";
            currentTranslations["ui.manualConnect.title"] = "Manual IP and Port Connection:";
            currentTranslations["ui.manualConnect.ip"] = "IP:";
            currentTranslations["ui.manualConnect.port"] = "Port:";
            currentTranslations["ui.manualConnect.button"] = "Manual Connect";
            currentTranslations["ui.manualConnect.portError"] = "Invalid port format";
            currentTranslations["ui.status.label"] = "Status:";
            currentTranslations["ui.status.notConnected"] = "Not Connected";
            currentTranslations["ui.status.connecting"] = "Connecting...";
            currentTranslations["ui.status.connected"] = "Connected";
            currentTranslations["ui.server.listenPort"] = "Server Listening Port:";
            currentTranslations["ui.server.connections"] = "Current Connections:";
            currentTranslations["ui.playerStatus.toggle"] = "Show Player Status Window (Toggle key: {0})";
            currentTranslations["ui.playerStatus.id"] = "ID:";
            currentTranslations["ui.playerStatus.name"] = "Name:";
            currentTranslations["ui.playerStatus.latency"] = "Latency:";
            currentTranslations["ui.playerStatus.inGame"] = "In Game:";
            currentTranslations["ui.playerStatus.yes"] = "Yes";
            currentTranslations["ui.playerStatus.no"] = "No";
            currentTranslations["ui.debug.printLootBoxes"] = "[Debug] Print all lootboxes in this map";
            currentTranslations["ui.vote.mapVote"] = "Map Vote / Ready  [{0}]";
            currentTranslations["ui.vote.pressKey"] = "Press {0} to toggle ready (Current: {1})";
            currentTranslations["ui.vote.ready"] = "Ready";
            currentTranslations["ui.vote.notReady"] = "Not Ready";
            currentTranslations["ui.vote.playerReadyStatus"] = "Player Ready Status:";
            currentTranslations["ui.vote.readyIcon"] = "✅ Ready";
            currentTranslations["ui.vote.notReadyIcon"] = "⌛ Not Ready";
            currentTranslations["ui.spectator.mode"] = "Spectator Mode: LMB ▶ Next | RMB ◀ Previous | Press F8 to end and view results (failsafe)";
            currentTranslations["ui.settings.friendlyFirePlayers"] = "Friendly Fire (Players)";
            currentTranslations["ui.settings.friendlyFirePlayers.desc"] = "Host only. When enabled, new and existing player proxies are set to the neutral team so friendly fire is possible; disabling restores everyone to the Player team.";
            currentTranslations["ui.settings.themeMode"] = "UI Theme";
            currentTranslations["ui.settings.themeMode.desc"] = "Switches the co-op panel theme locally.";
            currentTranslations["ui.settings.theme.white"] = "White";
            currentTranslations["ui.settings.theme.black"] = "Black";
            currentTranslations["ui.settings.theme.spring"] = "Spring";
            currentTranslations["ui.settings.theme.summer"] = "Summer";
            currentTranslations["ui.difficulty.spawnBonus"] = "Enemy Spawn Bonus (additive)";
            currentTranslations["ui.difficulty.spawnBonus.desc"] = "Adds to the base enemy spawn multiplier (e.g., +1 doubles spawns).";
            currentTranslations["ui.difficulty.forceBoss"] = "Force Boss Spawn";
            currentTranslations["ui.difficulty.forceBoss.desc"] = "Only Impossible and custom difficulties should toggle this on.";
            currentTranslations["vehicle.control.title"] = "Vehicle Control";
            currentTranslations["vehicle.control.current"] = "Controller: {0}";
            currentTranslations["vehicle.control.unassigned"] = "Assigning";
            currentTranslations["vehicle.control.riders"] = "Riders: {0}";
            currentTranslations["vehicle.control.requester"] = "Requester: {0}";
            currentTranslations["vehicle.control.key"] = "F2";
            currentTranslations["vehicle.control.action.approve"] = "Approve control request";
            currentTranslations["vehicle.control.action.controlling"] = "You are controlling this vehicle";
            currentTranslations["vehicle.control.action.pending"] = "Request sent, waiting for approval";
            currentTranslations["vehicle.control.action.request"] = "Request control";
            currentTranslations["vehicle.control.tip.approved"] = "Approved {0} to control the vehicle";
            currentTranslations["vehicle.control.tip.requested"] = "Vehicle control requested. Wait for the current controller to press F2";
            currentTranslations["vehicle.control.tip.incomingRequest"] = "{0} requests vehicle control. Press F2 to approve";

            // Scene 관련
            currentTranslations["scene.waitingForHost"] = "[Coop] Waiting for host to finish loading… (Auto-enter after 100s if delayed)";
            currentTranslations["scene.hostReady"] = "Host ready, entering…";

            // Network 관련
            currentTranslations["net.connectionSuccess"] = "Connected successfully: {0}";
            currentTranslations["net.connectedTo"] = "Connected to {0}";
            currentTranslations["net.disconnected"] = "Disconnected: {0}, Reason: {1}";
            currentTranslations["net.connectionLost"] = "Connection Lost";
            currentTranslations["net.networkError"] = "Network error: {0} from {1}";
            currentTranslations["net.hostDiscovered"] = "Host discovered: {0}";
            currentTranslations["net.serverStarted"] = "Server started, listening on port {0}";
            currentTranslations["net.serverStartFailed"] = "Server start failed, check if port is already in use";
            currentTranslations["net.clientStarted"] = "Client started";
            currentTranslations["net.clientStartFailed"] = "Client start failed";
            currentTranslations["net.networkStarted"] = "Network started";
            currentTranslations["net.networkStopped"] = "Network stopped";
            currentTranslations["net.ipEmpty"] = "IP is empty";
            currentTranslations["net.invalidPort"] = "Invalid port";
            currentTranslations["net.serverModeCannotConnect"] = "Server mode cannot connect to other hosts";
            currentTranslations["net.alreadyConnecting"] = "Already connecting.";
            currentTranslations["net.clientNetworkStartFailed"] = "Failed to start client network: {0}";
            currentTranslations["net.clientNetworkStartFailedStatus"] = "Client network start failed";
            currentTranslations["net.clientNotStarted"] = "Client not started";
            currentTranslations["net.connectingTo"] = "Connecting to: {0}:{1}";
            currentTranslations["net.connectionFailedLog"] = "Failed to connect to host: {0}";
            currentTranslations["net.connectionFailed"] = "Connection failed";

            // AI sync tuning
            currentTranslations["ui.aiSettings.title"] = "Co-op AI Sync";
            currentTranslations["ui.aiSettings.toggleHint"] = "{0} Show/Hide";
            currentTranslations["ui.aiSettings.section.distance.title"] = "Sync Distance";
            currentTranslations["ui.aiSettings.section.distance.subtitle"] = "Control when AI wakes or sleeps";
            currentTranslations["ui.aiSettings.activationRadius"] = "Activation Radius";
            currentTranslations["ui.aiSettings.deactivationRadius"] = "Sleep Radius";
            currentTranslations["ui.aiSettings.section.pacing.title"] = "Sync Rhythm";
            currentTranslations["ui.aiSettings.section.pacing.subtitle"] = "Broadcast and sampling cadence";
            currentTranslations["ui.aiSettings.activationRetryInterval"] = "Activation Retry Interval";
            currentTranslations["ui.aiSettings.stateBroadcastInterval"] = "State Broadcast Interval";
            currentTranslations["ui.aiSettings.idleStateRecordInterval"] = "Idle Sample Interval";
            currentTranslations["ui.aiSettings.healthBroadcastInterval"] = "Health Broadcast Interval";
            currentTranslations["ui.aiSettings.section.precision.title"] = "Interpolation & Precision";
            currentTranslations["ui.aiSettings.section.precision.subtitle"] = "Thresholds and velocity smoothing";
            currentTranslations["ui.aiSettings.minPositionDelta"] = "Min Position Delta";
            currentTranslations["ui.aiSettings.minRotationDelta"] = "Min Rotation Delta";
            currentTranslations["ui.aiSettings.velocityLerp"] = "Velocity Lerp";
            currentTranslations["ui.aiSettings.section.snapshot.title"] = "Snapshots & Queues";
            currentTranslations["ui.aiSettings.section.snapshot.subtitle"] = "Host-side refresh, queue caps, and drop recovery";
            currentTranslations["ui.aiSettings.snapshotRefreshInterval"] = "Snapshot Refresh Interval";
            currentTranslations["ui.aiSettings.snapshotRequestTimeout"] = "Snapshot Request Timeout";
            currentTranslations["ui.aiSettings.snapshotRecoveryCooldown"] = "Snapshot Recovery Cooldown";
            currentTranslations["ui.aiSettings.snapshotChunkSize"] = "Snapshot Chunk Size";
            currentTranslations["ui.aiSettings.maxStoredBuffs"] = "Max Stored Buffs";
            currentTranslations["ui.aiSettings.maxSnapshotAppliesPerFrame"] = "Max Snapshot Applies/Frame";
            currentTranslations["ui.aiSettings.maxStateUpdatesPerFrame"] = "Max State Updates/Frame";
            currentTranslations["ui.aiSettings.maxPendingSnapshotQueue"] = "Snapshot Queue Limit";
            currentTranslations["ui.aiSettings.maxPendingStateQueue"] = "State Queue Limit";
            currentTranslations["ui.aiSettings.snapshotDropResyncThreshold"] = "Snapshot Drop Resync Threshold";
            currentTranslations["ui.aiSettings.stateDropResyncThreshold"] = "State Drop Resync Threshold";
            currentTranslations["ui.aiSettings.section.hostOnly.title"] = "Host Only";
            currentTranslations["ui.aiSettings.section.hostOnly.subtitle"] = "Only the host’s changes take effect";
            currentTranslations["ui.aiSettings.serverControllerRescanInterval"] = "Controller Rescan Interval";
            currentTranslations["ui.aiSettings.serverSnapshotBroadcastInterval"] = "Snapshot Broadcast Interval";
            currentTranslations["ui.aiSettings.serverSnapshotRetryInterval"] = "Snapshot Retry Interval";
            currentTranslations["ui.aiSettings.badge.hostOnly"] = "Host Only";
            currentTranslations["ui.aiSettings.badge.hostOnlyShort"] = "Host";

            // Waiting screen
            currentTranslations["ui.waiting.initializing"] = "Initializing...";
            currentTranslations["ui.waiting.syncComplete"] = "Sync complete!";
            currentTranslations["ui.waiting.map"] = "Map: {0}";
            currentTranslations["ui.waiting.time"] = "Time: Day {0} {1:D2}:{2:D2}";
            currentTranslations["ui.waiting.timeUnknown"] = "Time: --:--";
            currentTranslations["ui.waiting.weather"] = "Weather: {0}";
            currentTranslations["ui.waiting.weatherUnknown"] = "Weather: Unknown";
            currentTranslations["ui.waiting.loadingPlayers"] = "Loading player list...";
        }

        /// <summary>
        /// 번역된 문자열 가져오기
        /// </summary>
        /// <param name="key">번역 키</param>
        /// <param name="args">포맷 인자</param>
        /// <returns>번역된 문자열</returns>
        public static string Get(string key, params object[] args)
        {
            if (!isInitialized)
            {
                Initialize();
            }

            if (currentTranslations.TryGetValue(key, out string value))
            {
                if (args != null && args.Length > 0)
                {
                    try
                    {
                        return string.Format(value, args);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CoopLocalization] Format error for key '{key}': {e.Message}");
                        return value;
                    }
                }
                return value;
            }

            Debug.LogWarning($"[CoopLocalization] Missing translation for key: {key}");
            return $"[{key}]";
        }

        /// <summary>
        /// 언어 변경
        /// </summary>
        /// <param name="languageCode">언어 코드 (zh-CN, en-US, ko-KR, ja-JP)</param>
        public static void SetLanguage(string languageCode)
        {
            if (currentLanguageCode == languageCode) return;

            currentLanguageCode = languageCode;
            LoadTranslations(languageCode);
            Debug.Log($"[CoopLocalization] Language changed to: {languageCode}");
        }

        /// <summary>
        /// 현재 언어 코드 가져오기
        /// </summary>
        public static string GetCurrentLanguage()
        {
            return currentLanguageCode;
        }
    }
}
