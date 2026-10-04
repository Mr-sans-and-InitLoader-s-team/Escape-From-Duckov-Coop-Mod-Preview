using EscapeFromDuckovCoopMod;
using Duckov.Economy;

static class Program
{
    static CoopPeer Peer;
    static string Token;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Start(bool unlocked = true)
    {
        TeleporterTravel.Reset();
        NetService.Instance = new(); SceneNet.Instance = new(); Peer = new();
        NetService.Instance.playerStatuses.Add(Peer, new PlayerStatus());
        EconomyManager.Money = 100; ItemUtilities.Items = 5;
        Cost.Pays = Cost.Refunds = 0; Cost.FailAfterMoney = false;
        Time.unscaledTime = 0; CoopTool.Messages.Clear(); TravelNotice.Message = null;
        TeleporterDepartureView.Shows = 0; TeleporterDepartureView.WasClosed = false;
        Duckov.UI.MapSelectionView.Instance.Entry.ConditionsSatisfied = unlocked;
        Token = TeleporterTravel.BeginHost("map", 2, new[] { "host", "client" });
        TeleporterTravel.VotesReady();
    }
    static void Reply(TeleporterFlowKind kind, bool success = true, string token = null)
    {
        TeleporterTravel.Handle(new RpcContext(Peer), new TeleporterFlowRpc { Kind = kind, Token = token ?? Token, Success = success, Reason = success ? null : "ui.teleporter.reason.missingCost" });
    }
    static void ConfirmBoth()
    {
        Reply(TeleporterFlowKind.Checked);
        TeleporterTravel.ConfirmLocal();
        Reply(TeleporterFlowKind.Confirmed);
    }
    static void Main()
    {
        Start();
        Check(TeleporterDepartureView.Shows == 0 && Cost.Pays == 0, "opened/paid before all checks");
        Reply(TeleporterFlowKind.Checked, false);
        Check(!TeleporterTravel.IsActive && SceneNet.Instance.Loads == 0 && Cost.Pays == 0 && TeleporterDepartureView.Shows == 0, "missing cost did not abort before departure");
        Check(TravelNotice.Message.Contains("Client"), "missing player's name not shown");
        Console.WriteLine("PASS any missing player aborts before departure UI, payment and loading");

        Start(); ConfirmBoth();
        Check(Cost.Pays == 1 && SceneNet.Instance.Loads == 0, "load before all paid");
        Reply(TeleporterFlowKind.Confirmed); // duplicate button/reply
        Check(Cost.Pays == 1, "duplicate payment");
        Reply(TeleporterFlowKind.Paid);
        Reply(TeleporterFlowKind.Paid);
        Check(SceneNet.Instance.Loads == 1 && TeleporterTravel.AcceptCommit(Token), "commit did not require everyone or ran twice");
        TeleporterTravel.Reset();
        Check(EconomyManager.Money == 90 && ItemUtilities.Items == 2 && Cost.Refunds == 0, "committed native payment was refunded");
        Console.WriteLine("PASS native payment runs once; all-paid barrier commits exactly once");

        Start(); ConfirmBoth(); Reply(TeleporterFlowKind.Paid, false);
        Check(SceneNet.Instance.Loads == 0 && EconomyManager.Money == 100 && ItemUtilities.Items == 5 && Cost.Refunds == 1, "partial party payment was not refunded");
        Reply(TeleporterFlowKind.Paid, false); TeleporterTravel.Reset();
        Check(Cost.Refunds == 1, "cancellation refunded twice");
        Console.WriteLine("PASS another player's payment failure refunds successful native deduction once");

        Start(); Reply(TeleporterFlowKind.Checked);
        ItemUtilities.Items = 0; TeleporterTravel.ConfirmLocal();
        Check(!TeleporterTravel.IsActive && Cost.Pays == 0 && SceneNet.Instance.Loads == 0, "cost loss between vote and confirm was ignored");
        Console.WriteLine("PASS items disappearing during confirmation cancel everyone");

        Start(); Cost.FailAfterMoney = true; ConfirmBoth();
        Check(!TeleporterTravel.IsActive && EconomyManager.Money == 100 && ItemUtilities.Items == 5 && Cost.Refunds == 1, "native partial Pay failure minted items or lost money");
        Console.WriteLine("PASS failed native Pay returns actual deductions only");

        Start(); ConfirmBoth(); TeleporterTravel.PeerDisconnected(Peer);
        Check(!TeleporterTravel.IsActive && Cost.Refunds == 1 && SceneNet.Instance.Loads == 0, "disconnect allowed travel");
        Console.WriteLine("PASS disconnect cancels and refunds pending travel");

        Start(); Reply(TeleporterFlowKind.Checked, true, "previous-round");
        Check(TeleporterDepartureView.Shows == 0, "old-round reply accepted");
        Time.unscaledTime = 21; TeleporterTravel.Tick();
        Check(!TeleporterTravel.IsActive && SceneNet.Instance.Loads == 0, "timed out check was not cancelled");
        Console.WriteLine("PASS stale replies and timeout cannot advance a round");

        Start(); Reply(TeleporterFlowKind.Checked);
        TeleporterDepartureView.WasClosed = true; TeleporterTravel.Tick();
        Check(!TeleporterTravel.IsActive && Cost.Pays == 0, "closing confirmation did not cancel");
        Console.WriteLine("PASS closed confirmation cancels before payment");
        Start();
        Check(TeleporterTravel.WithdrawReady("client") && !TeleporterTravel.IsActive && Cost.Pays == 0, "withdrawn vote advanced to departure");
        Reply(TeleporterFlowKind.Checked);
        Check(TeleporterDepartureView.Shows == 0 && SceneNet.Instance.Loads == 0, "late check revived withdrawn vote");
        Console.WriteLine("PASS withdrawing readiness cancels checking and ignores late success replies");
        Start(unlocked: false); ConfirmBoth(); Reply(TeleporterFlowKind.Paid);
        Check(TeleporterTravel.AcceptCommit(Token) && SceneNet.Instance.Loads == 1 && Cost.Pays == 1, "locked destination rejected despite sufficient native cost");
        Console.WriteLine("PASS locked destination can depart and pay native cost normally");

        CoopLocalization.Language = "en-US";
        Start(); Reply(TeleporterFlowKind.Checked, false);
        var abort = CoopTool.Messages.Last(m => m.Kind == TeleporterFlowKind.Aborted);
        Check(abort.Reason == "ui.teleporter.reason.missingCost" && abort.PlayerName == "Client", "wire contains host-language text instead of reason key");
        Check(TravelNotice.Message.Contains("required items or money"), "English host message");
        CoopLocalization.Language = "zh-CN";
        var translated = TravelMessages.Cancellation(abort.PlayerName, abort.Reason);
        Check(translated.Contains("缺少传送所需物品或货币") && translated.Contains("Client"), "recipient language not used");
        foreach (var language in new[] { "en-US", "zh-CN", "ja-JP", "ko-KR", "de-DE", "pt-BR", "ru-RU" })
        {
            CoopLocalization.Language = language;
            Check(TravelMessages.Cancellation("Client", abort.Reason).Contains("Client"), "missing locale cancellation format");
        }
        Console.WriteLine("PASS recipients localize reason keys independently in all seven languages");
        Console.WriteLine("11 teleporter regression scenarios passed (native UI/economy test doubles).");
    }
}
