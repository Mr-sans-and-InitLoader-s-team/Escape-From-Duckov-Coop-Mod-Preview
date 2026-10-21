namespace EscapeFromDuckovCoopMod;

internal static class TravelMessages
{
    public static string Cancellation(string playerName, string reasonKey)
    {
        if (string.IsNullOrEmpty(playerName)) return CoopLocalization.Get("ui.teleporter.cancelled");
        if (string.IsNullOrEmpty(reasonKey) || !reasonKey.StartsWith("ui.teleporter.reason.", StringComparison.Ordinal))
            reasonKey = "ui.teleporter.reason.requirementsFailed";
        return CoopLocalization.Get("ui.teleporter.cancelledByPlayer", playerName, CoopLocalization.Get(reasonKey));
    }
}
