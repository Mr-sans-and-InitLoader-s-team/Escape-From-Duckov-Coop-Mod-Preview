namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeDisconnect
{
    private static bool _returning;

    internal static void ReturnToMenu(NetService service, NetManager disconnectedNetwork)
    {
        if (_returning) return;
        Return(service, disconnectedNetwork).Forget();
    }

    private static async UniTaskVoid Return(NetService service, NetManager disconnectedNetwork)
    {
        _returning = true;
        MModUI.Instance?.ShowDisconnectNotice(true);
        try
        {
            // Finish transport callbacks before stopping the transport or unloading its scene.
            await UniTask.NextFrame();
            if (!service || service.IsServer || service.isConnecting ||
                service.connectedPeer?.ConnectionState == ConnectionState.Connected ||
                service.networkStarted && service.netManager != disconnectedNetwork) return;
            ConnectionRecovery.Cleanup(() => service.StopNetwork());
            await UniTask.WaitUntil(() => !SceneLoader.IsSceneLoading && !SharedHome.Reloading && !SharedHomeLoadouts.Restoring);
            if (!service || service.networkStarted || SharedHomeEntry.AtMainMenu) return;
            SceneLoader.LoadMainMenu();
        }
        catch (Exception e) { Debug.LogError($"[SharedHome] Return to menu after disconnect failed: {e}"); }
        finally { _returning = false; }
    }
}
