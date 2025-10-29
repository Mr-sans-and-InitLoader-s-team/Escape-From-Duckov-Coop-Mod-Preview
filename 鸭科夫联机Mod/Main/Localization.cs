using System;
using System.Collections.Generic;
using SodaCraft.Localizations;
using UnityEngine;

namespace 鸭科夫联机Mod.Main
{
    /// <summary>
    /// Mod 다국어 지원 시스템
    /// 게임의 언어 설정을 감지하고 UI 텍스트를 해당 언어로 변환
    /// </summary>
    public static class Localization
    {
        // 다국어 문자열 딕셔너리 (SystemLanguage 기반)
        private static Dictionary<string, Dictionary<SystemLanguage, string>> translations;

        // 초기화 여부
        private static bool initialized = false;

        /// <summary>
        /// 초기화
        /// </summary>
        public static void Initialize()
        {
            if (initialized) return;

            InitializeTranslations();
            initialized = true;
            Debug.Log($"[COOP Localization] 초기화 완료, 현재 언어: {CurrentLanguage}");
        }

        /// <summary>
        /// 현재 게임 언어 가져오기
        /// </summary>
        public static SystemLanguage CurrentLanguage
        {
            get
            {
                try
                {
                    return LocalizationManager.CurrentLanguage;
                }
                catch
                {
                    return SystemLanguage.ChineseSimplified;
                }
            }
        }

        /// <summary>
        /// 중국어 여부 확인
        /// </summary>
        private static bool IsChinese(SystemLanguage lang)
        {
            return lang == SystemLanguage.Chinese ||
                   lang == SystemLanguage.ChineseSimplified ||
                   lang == SystemLanguage.ChineseTraditional;
        }

        /// <summary>
        /// 번역 문자열 가져오기
        /// </summary>
        /// <param name="key">번역 키</param>
        /// <returns>현재 언어로 번역된 문자열</returns>
        public static string Get(string key)
        {
            if (!initialized)
                Initialize();

            var lang = CurrentLanguage;

            // 중국어면 원본 그대로 반환 (이미 중국어로 되어 있음)
            if (IsChinese(lang))
            {
                if (translations.TryGetValue(key, out var langDict))
                {
                    if (langDict.TryGetValue(SystemLanguage.ChineseSimplified, out var text))
                        return text;
                }
            }
            else
            {
                // 다른 언어로 번역 시도
                if (translations.TryGetValue(key, out var langDict))
                {
                    // 현재 언어 번역이 있으면 반환
                    if (langDict.TryGetValue(lang, out var text))
                        return text;

                    // 영어 폴백
                    if (langDict.TryGetValue(SystemLanguage.English, out var englishText))
                        return englishText;

                    // 중국어 폴백
                    if (langDict.TryGetValue(SystemLanguage.ChineseSimplified, out var chineseText))
                        return chineseText;
                }
            }

            Debug.LogWarning($"[COOP Localization] 번역 키를 찾을 수 없음: {key}");
            return key;
        }

        /// <summary>
        /// 포맷팅된 번역 문자열 가져오기
        /// </summary>
        public static string GetFormatted(string key, params object[] args)
        {
            string text = Get(key);
            try
            {
                return string.Format(text, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[COOP Localization] 문자열 포맷팅 실패: {key}, {ex.Message}");
                return text;
            }
        }

        /// <summary>
        /// 모든 번역 문자열 초기화
        /// </summary>
        private static void InitializeTranslations()
        {
            translations = new Dictionary<string, Dictionary<SystemLanguage, string>>();

            // UI 텍스트 번역 추가
            AddTranslation("status_disconnected",
                zh: "未连接",
                ko: "연결 안됨",
                en: "Disconnected",
                ja: "未接続"
            );

            AddTranslation("status_network_started",
                zh: "网络已启动",
                ko: "네트워크 시작됨",
                en: "Network Started",
                ja: "ネットワーク起動"
            );

            AddTranslation("status_network_stopped",
                zh: "网络已停止",
                ko: "네트워크 중지됨",
                en: "Network Stopped",
                ja: "ネットワーク停止"
            );

            AddTranslation("ui_main_window_title",
                zh: "联机Mod控制面板",
                ko: "멀티플레이 Mod 제어판",
                en: "Co-op Mod Control Panel",
                ja: "マルチプレイMod制御パネル"
            );

            AddTranslation("ui_player_status_title",
                zh: "玩家状态",
                ko: "플레이어 상태",
                en: "Player Status",
                ja: "プレイヤー状態"
            );

            AddTranslation("ui_mode_server",
                zh: "服务器",
                ko: "서버",
                en: "Server",
                ja: "サーバー"
            );

            AddTranslation("ui_mode_client",
                zh: "客户端",
                ko: "클라이언트",
                en: "Client",
                ja: "クライアント"
            );

            AddTranslation("ui_current_mode",
                zh: "当前模式",
                ko: "현재 모드",
                en: "Current Mode",
                ja: "現在のモード"
            );

            AddTranslation("ui_switch_to_mode",
                zh: "切换到{0}模式",
                ko: "{0} 모드로 전환",
                en: "Switch to {0} Mode",
                ja: "{0}モードに切替"
            );

            AddTranslation("ui_lan_host_list",
                zh: "🔍 局域网主机列表",
                ko: "🔍 LAN 호스트 목록",
                en: "🔍 LAN Host List",
                ja: "🔍 LANホストリスト"
            );

            AddTranslation("ui_no_hosts_found",
                zh: "（等待广播回应，暂无主机）",
                ko: "(브로드캐스트 응답 대기 중, 호스트 없음)",
                en: "(Waiting for broadcast, no hosts found)",
                ja: "(ブロードキャスト応答待ち、ホストなし)"
            );

            AddTranslation("ui_button_connect",
                zh: "连接",
                ko: "연결",
                en: "Connect",
                ja: "接続"
            );

            AddTranslation("ui_manual_connect",
                zh: "手动输入 IP 和端口连接:",
                ko: "IP와 포트를 수동으로 입력:",
                en: "Manual IP and Port:",
                ja: "IPとポートを手動入力:"
            );

            AddTranslation("ui_port_label",
                zh: "端口:",
                ko: "포트:",
                en: "Port:",
                ja: "ポート:"
            );

            AddTranslation("ui_button_manual_connect",
                zh: "手动连接",
                ko: "수동 연결",
                en: "Manual Connect",
                ja: "手動接続"
            );

            AddTranslation("status_connected_to",
                zh: "已连接到 {0}",
                ko: "{0}에 연결됨",
                en: "Connected to {0}",
                ja: "{0}に接続済み"
            );

            AddTranslation("status_connection_closed",
                zh: "连接断开",
                ko: "연결 끊김",
                en: "Connection Closed",
                ja: "接続切断"
            );

            AddTranslation("status_ip_empty",
                zh: "IP为空",
                ko: "IP가 비어있음",
                en: "IP is empty",
                ja: "IPが空です"
            );

            AddTranslation("status_port_invalid",
                zh: "端口不合法",
                ko: "포트가 유효하지 않음",
                en: "Invalid port",
                ja: "無効なポート"
            );

            AddTranslation("status_port_format_error",
                zh: "端口格式错误",
                ko: "포트 형식 오류",
                en: "Port format error",
                ja: "ポート形式エラー"
            );

            AddTranslation("status_connecting",
                zh: "连接中: {0}:{1}",
                ko: "연결 중: {0}:{1}",
                en: "Connecting: {0}:{1}",
                ja: "接続中: {0}:{1}"
            );

            AddTranslation("status_connection_failed",
                zh: "连接失败",
                ko: "연결 실패",
                en: "Connection Failed",
                ja: "接続失敗"
            );

            AddTranslation("status_client_not_started",
                zh: "客户端未启动",
                ko: "클라이언트가 시작되지 않음",
                en: "Client not started",
                ja: "クライアント未起動"
            );

            AddTranslation("status_client_network_failed",
                zh: "客户端网络启动失败",
                ko: "클라이언트 네트워크 시작 실패",
                en: "Client network start failed",
                ja: "クライアントネットワーク起動失敗"
            );

            AddTranslation("ui_scene_vote_ready",
                zh: "地图投票 / 准备",
                ko: "맵 투표 / 준비",
                en: "Map Vote / Ready",
                ja: "マップ投票 / 準備"
            );

            AddTranslation("ui_ready_status",
                zh: "按 {0} 切换准备（当前：{1}）",
                ko: "{0}를 눌러 준비 전환 (현재: {1})",
                en: "Press {0} to toggle ready (Current: {1})",
                ja: "{0}で準備切替 (現在: {1})"
            );

            AddTranslation("ui_ready",
                zh: "已准备",
                ko: "준비됨",
                en: "Ready",
                ja: "準備完了"
            );

            AddTranslation("ui_not_ready",
                zh: "未准备",
                ko: "준비 안됨",
                en: "Not Ready",
                ja: "未準備"
            );

            AddTranslation("ui_player_ready_status",
                zh: "玩家准备状态：",
                ko: "플레이어 준비 상태:",
                en: "Player Ready Status:",
                ja: "プレイヤー準備状態:"
            );

            AddTranslation("ui_ready_checkmark",
                zh: "✅ 就绪",
                ko: "✅ 준비됨",
                en: "✅ Ready",
                ja: "✅ 準備完了"
            );

            AddTranslation("ui_not_ready_hourglass",
                zh: "⌛ 未就绪",
                ko: "⌛ 준비 안됨",
                en: "⌛ Not Ready",
                ja: "⌛ 未準備"
            );

            AddTranslation("ui_spectator_mode",
                zh: "观战模式：左键 ▶ 下一个 | 右键 ◀ 上一个  | 正在观战",
                ko: "관전 모드: 좌클릭 ▶ 다음 | 우클릭 ◀ 이전 | 관전 중",
                en: "Spectator Mode: LClick ▶ Next | RClick ◀ Prev | Spectating",
                ja: "観戦モード：左クリック ▶ 次 | 右クリック ◀ 前 | 観戦中"
            );

            AddTranslation("ui_status_label",
                zh: "状态:",
                ko: "상태:",
                en: "Status:",
                ja: "状態:"
            );

            AddTranslation("ui_server_port",
                zh: "服务器监听端口: {0}",
                ko: "서버 수신 포트: {0}",
                en: "Server Listening Port: {0}",
                ja: "サーバー待受ポート: {0}"
            );

            AddTranslation("ui_connection_count",
                zh: "当前连接数: {0}",
                ko: "현재 연결 수: {0}",
                en: "Current Connections: {0}",
                ja: "現在の接続数: {0}"
            );

            AddTranslation("ui_debug_print_lootboxes",
                zh: "[Debug] 打印出该地图的所有lootbox",
                ko: "[Debug] 이 맵의 모든 lootbox 출력",
                en: "[Debug] Print all lootboxes in this map",
                ja: "[Debug] このマップの全ルートボックスを出力"
            );

            AddTranslation("ui_toggle_player_status_window",
                zh: "显示玩家状态窗口 (切换键: {0})",
                ko: "플레이어 상태 창 표시 (토글 키: {0})",
                en: "Show Player Status Window (Toggle: {0})",
                ja: "プレイヤー状態ウィンドウ表示 (切替: {0})"
            );

            AddTranslation("ui_player_name_label",
                zh: "名称:",
                ko: "이름:",
                en: "Name:",
                ja: "名前:"
            );

            AddTranslation("ui_player_latency_label",
                zh: "延迟:",
                ko: "지연:",
                en: "Latency:",
                ja: "レイテンシ:"
            );

            AddTranslation("ui_player_in_game_label",
                zh: "游戏中:",
                ko: "게임 중:",
                en: "In Game:",
                ja: "ゲーム中:"
            );

            AddTranslation("ui_yes",
                zh: "是",
                ko: "예",
                en: "Yes",
                ja: "はい"
            );

            AddTranslation("ui_no",
                zh: "否",
                ko: "아니오",
                en: "No",
                ja: "いいえ"
            );
        }

        /// <summary>
        /// 번역 추가 헬퍼 메서드
        /// </summary>
        private static void AddTranslation(string key, string zh, string ko, string en, string ja)
        {
            var langDict = new Dictionary<SystemLanguage, string>
            {
                { SystemLanguage.ChineseSimplified, zh },
                { SystemLanguage.Chinese, zh },
                { SystemLanguage.ChineseTraditional, zh },
                { SystemLanguage.Korean, ko },
                { SystemLanguage.English, en },
                { SystemLanguage.Japanese, ja }
            };
            translations[key] = langDict;
        }
    }
}
