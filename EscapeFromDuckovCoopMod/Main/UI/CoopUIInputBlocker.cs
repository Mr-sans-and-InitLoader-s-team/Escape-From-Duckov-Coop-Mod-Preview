namespace EscapeFromDuckovCoopMod;

internal sealed class CoopUIInputBlocker : MonoBehaviour
{
    private void OnEnable() => Update();

    private void Update()
    {
        InputManager.DisableInput(gameObject);
    }

    private void OnDisable()
    {
        if (LevelManager.Instance && LevelManager.Instance.InputManager)
            InputManager.ActiveInput(gameObject);
    }
}
