using Duckov.UI;
using Duckov.UI.Animations;
using Duckov.Economy;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal static class TeleporterDepartureView
{
    private static MapSelectionView _view;
    private static Button _confirm;
    private static Button _cancel;
    public static bool IsOpen => _view != null;

    public static bool Show(MapSelectionEntry entry)
    {
        Close();
        _view = MapSelectionView.Instance;
        if (_view == null || entry == null) return false;
        if (Traverse.Create(_view).Field<bool>("loading").Value) { _view = null; return false; }
        _view.Open();
        var fields = Traverse.Create(_view);
        fields.Field<bool>("loading").Value = true;
        AccessTools.Method(typeof(MapSelectionView), "SetupSceneInfo").Invoke(_view, new object[] { SceneInfoCollection.GetSceneInfo(entry.SceneID) });
        var cost = fields.Field<CostDisplay>("confirmCostDisplay").Value;
        cost.Setup(entry.Cost);
        cost.gameObject.SetActive(!entry.Cost.IsFree);
        _confirm = fields.Field<Button>("btnConfirm").Value;
        _cancel = fields.Field<Button>("btnCancel").Value;
        _confirm.interactable = true;
        _confirm.gameObject.SetActive(true);
        _cancel.gameObject.SetActive(true);
        _cancel.interactable = true;
        _confirm.onClick.AddListener(Confirm);
        _cancel.onClick.AddListener(Cancel);
        fields.Field<FadeGroup>("confirmIndicatorFadeGroup").Value.Show();
        return true;
    }
    public static bool WasClosed => _view != null && View.ActiveView != _view;
    public static void LockCancellation() { if (_cancel != null) _cancel.interactable = false; }
    private static void Confirm()
    {
        if (_confirm != null) _confirm.interactable = false;
        TeleporterTravel.ConfirmLocal();
    }
    private static void Cancel() => TeleporterTravel.CancelLocal("ui.teleporter.reason.cancelled");
    public static void Close()
    {
        if (_confirm != null) { _confirm.onClick.RemoveListener(Confirm); _confirm.interactable = true; }
        if (_cancel != null) { _cancel.onClick.RemoveListener(Cancel); _cancel.interactable = true; }
        if (_view != null)
        {
            Traverse.Create(_view).Field<bool>("loading").Value = false;
            _view.Close();
        }
        _view = null;
        _confirm = _cancel = null;
    }
}
