using Steamworks;

namespace EscapeFromDuckovCoopMod;

internal static class ConnectionRecovery
{
    private static string _ip;
    private static int _port, _generation, _stopDepth, _preserveDepth;
    private static ulong _steamHost, _lobby;
    private static bool _performingAttempt;
    private static bool _skipDisconnect;
    internal static bool Pending { get; private set; }
    internal static int Attempt { get; private set; }
    internal static float NextAttemptAt { get; private set; }

    internal static void RecordDirect(string ip, int port)
    {
        if (!_performingAttempt) Cancel();
        _ip = ip; _port = port; _steamHost = _lobby = 0;
        _skipDisconnect = false;
    }
    internal static void RecordSteam(CSteamID host)
    {
        if (_steamHost != host.m_SteamID) Cancel();
        _steamHost = host.m_SteamID;
        _lobby = SteamLobbyManager.Instance?.CurrentLobbyId.m_SteamID ?? 0;
        _ip = null;
        _skipDisconnect = false;
    }
    internal static void Connected() => Cancel();
    internal static void SkipNextDisconnect() { _skipDisconnect = true; Cancel(); }
    internal static void Cancel() { Pending = false; _generation++; }
    internal static void CancelAndStop()
    {
        Cancel();
        NetService.Instance?.StopNetwork();
    }
    internal static IDisposable Stopping()
    {
        if (_preserveDepth == 0) Cancel();
        _stopDepth++;
        return new StopScope();
    }
    private sealed class StopScope : IDisposable { public void Dispose() => _stopDepth--; }
    internal static void Cleanup(Action action)
    {
        _preserveDepth++;
        try { action(); }
        finally { _preserveDepth--; }
    }
    internal static void Begin(bool shared)
    {
        if (_skipDisconnect) { _skipDisconnect = false; return; }
        if (_stopDepth > 0 || Pending || string.IsNullOrEmpty(_ip) && _steamHost == 0) return;
        Pending = true;
        Attempt = 0;
        Retry(++_generation, shared).Forget();
    }
    private static async UniTaskVoid Retry(int generation, bool shared)
    {
        bool Current() => Pending && generation == _generation;
        try
        {
            while (Current() && Attempt < 5)
            {
                NextAttemptAt = Time.unscaledTime + (Attempt == 0 ? 3f : 5f);
                await UniTask.WaitUntil(() => !Current() || Time.unscaledTime >= NextAttemptAt &&
                    !SceneLoader.IsSceneLoading && !SharedHome.Reloading && !SharedHomeLoadouts.Restoring &&
                    (!shared || SharedHomeEntry.AtMainMenu));
                if (!Current()) return;
                Attempt++;
                NextAttemptAt = 0;
                var service = NetService.Instance;
                if (!service) return;
                _performingAttempt = true;
                try
                {
                    Cleanup(() =>
                    {
                        service.StopNetwork();
                        if (_steamHost != 0)
                        {
                            if (_lobby != 0) SteamLobbyManager.Instance.JoinLobby(new CSteamID(_lobby));
                            else service.ConnectToSteamHost(new CSteamID(_steamHost));
                        }
                        else service.ConnectToHost(_ip, _port);
                    });
                }
                finally { _performingAttempt = false; }
                var deadline = Time.unscaledTime + 15f;
                await UniTask.WaitUntil(() => !Current() || Time.unscaledTime >= deadline ||
                    !service.isConnecting && SteamLobbyManager.Instance?.IsLobbyRequestPending != true);
                if (Current()) Cleanup(() => service.StopNetwork());
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Reconnect] {e}"); }
        finally { if (generation == _generation) Pending = false; }
    }
}
