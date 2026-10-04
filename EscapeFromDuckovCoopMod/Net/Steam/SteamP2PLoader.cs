namespace EscapeFromDuckovCoopMod;

public class SteamP2PLoader : MonoBehaviour
{
    public static SteamP2PLoader Instance { get; private set; }
    public bool UseSteamP2P;

    public void Init()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (SteamManager.Initialized && SteamLobbyManager.Instance == null)
            gameObject.AddComponent<SteamLobbyManager>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9)) SteamLobbyManager.Instance?.InviteFriend();
        if (Input.GetKeyDown(KeyCode.F10))
        {
            if (NetService.Instance?.netManager is SteamSocketsTransport steam) steam.LogConnections();
            ColdBuffSync.LogLocalState();
        }
    }
}
