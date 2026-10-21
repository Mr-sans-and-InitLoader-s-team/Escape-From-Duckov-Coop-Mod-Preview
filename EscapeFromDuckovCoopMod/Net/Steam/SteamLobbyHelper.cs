using Steamworks;

namespace EscapeFromDuckovCoopMod;

public static class SteamLobbyHelper
{
    public static void TriggerMultiplayerConnect(CSteamID hostSteamID)
    {
        NetService.Instance.ConnectToSteamHost(hostSteamID);
    }

    public static void TriggerMultiplayerHost()
    {
        var service = NetService.Instance;
        if (service.networkStarted && service.IsServer && service.TransportMode == NetworkTransportMode.SteamP2P)
            return;
        service.SetTransportMode(NetworkTransportMode.SteamP2P);
        service.StartNetwork(true, keepSteamLobby: true);
    }
}
