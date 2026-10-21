using System.IO;

namespace EscapeFromDuckovCoopMod;

#pragma warning disable CS0649 // Populated from the authored portrait asset.

[Serializable]
internal sealed class CoopModePresentation
{
    public CoopModePortrait independent, shared;
    public CoopModeBackdropStyle menuBackdrop;
    internal static CoopModePresentation Load()
    {
        using var stream = typeof(CoopModePresentation).Assembly.GetManifestResourceStream("EscapeFromDuckovCoopMod.ModePortraits.json");
        using var reader = new StreamReader(stream);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<CoopModePresentation>(reader.ReadToEnd());
    }
}

[Serializable]
internal sealed class CoopModePortrait
{
    public Vector3 cameraPosition, cameraTarget;
    public float fieldOfView;
    public Color background;
    public CoopModePortraitLight[] lights;
    public CoopModeDuckPose[] ducks;
    public CoopModeBackdropStyle backdrop;
}

[Serializable]
internal sealed class CoopModeBackdropStyle
{
    public Color ambient, floor, light, shadow;
    public Vector2 lightOrigin, lightDirection;
    public float lightWidth, lightLength, floorHeight, fadeTop;
    public CoopModeBackdropShadow[] shadows;
}

[Serializable]
internal sealed class CoopModeBackdropShadow
{
    public Vector2 center, size;
}

[Serializable]
internal sealed class CoopModePortraitLight
{
    public Vector3 position, rotation;
    public Color color;
    public float intensity, range;
    public bool directional;
}

[Serializable]
internal sealed class CoopModeDuckPose
{
    public Vector3 position, rotation;
    public Color color;
    public int helmet, armor, backpack, gun;
    public int eyes, mouth;
    public float hoverSpeed;
}
