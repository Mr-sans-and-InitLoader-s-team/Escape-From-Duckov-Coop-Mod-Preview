using UnityEngine.SceneManagement;

namespace EscapeFromDuckovCoopMod;

internal sealed class KazooSyncDriver : MonoBehaviour
{
    private void OnEnable() => SceneManager.sceneUnloaded += OnSceneUnloaded;
    private void Update() => KazooSync.Tick();
    private void OnSceneUnloaded(Scene scene) => KazooSync.SceneUnloaded();
    private void OnDisable()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        KazooSync.Reset();
    }
}
