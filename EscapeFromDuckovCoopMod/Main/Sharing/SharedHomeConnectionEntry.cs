namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeConnectionEntry
{
    internal static bool Check()
    {
        var service = NetService.Instance;
        if (!SharedHome.Guest || service.connectedPeer?.ConnectionState != ConnectionState.Connected) return true;
        service.status = CoopLocalization.Get("net.sharedHome.alreadyConnected");
        MModUI.ShowTip(service.status);
        return false;
    }
}
