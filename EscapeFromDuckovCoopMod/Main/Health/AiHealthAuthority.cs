using Duckov.Utilities;
using FX;

namespace EscapeFromDuckovCoopMod;

public sealed partial class AISyncService
{
    private static readonly System.Reflection.FieldInfo FinalDamageProcessors = AccessTools.Field(typeof(Health), "finalDamageProcessFuncs");
    private readonly HealthSnapshotOrder _clientHealthOrder = new();
    private readonly Dictionary<int, (AIHealthBroadcastRpc Message, long Order)> _clientPendingHealth = new();

    public void Client_HandleHealthBroadcast(AIHealthBroadcastRpc message)
    {
        if (IsServer || CheckAndHandleSyncBlock()) return;
        var order = _clientHealthOrder.Receive();
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
        {
            _clientPendingHealth[message.Id] = (message, order);
            Client_TryScheduleSnapshotRecovery(true);
            return;
        }
        Client_ApplyHealthBroadcast(entry, message, order);
    }

    private void Client_ApplyHealthBroadcast(AISyncEntry entry, AIHealthBroadcastRpc message, long order)
    {
        if (!_clientHealthOrder.TryApply(entry.Id, order)) return;
        if (message.MaxHealth > 0f) entry.MaxHealth = message.MaxHealth;
        entry.CurrentHealth = message.CurrentHealth;
        entry.BodyArmor = message.BodyArmor;
        entry.HeadArmor = message.HeadArmor;
        if (message.ConfirmedDamage > 0f && PopText.instance)
        {
            var look = GameplayDataSettings.UIStyle.GetElementDamagePopTextLook(ElementTypes.physics);
            var critical = message.Damage.Crit > 0;
            PopText.Pop(message.ConfirmedDamage.ToString("F1"), message.Damage.HitPoint + Vector3.up * 2f,
                look.color, critical ? look.critSize : look.normalSize,
                critical ? GameplayDataSettings.UIStyle.CritPopSprite : null);
        }
        if (message.ConfirmedDamage > 0f && message.HasDamage)
            LocalHitKillFx.ClientPlayConfirmedAiHit(message.Damage.ToDamageInfo(CharacterMainControl.Main));
        if (message.IsDead)
        {
            entry.Status = AIStatus.Dead;
            if (message.KillerCredit || message.AssistCredit) Client_RaiseQuestKillCredit(entry.Id, in message);
            Client_DestroyReplica(entry.Id, AIStatus.Dead);
            return;
        }
        if (_clientReplicas.TryGetValue(entry.Id, out var replica))
            replica.ApplyHealth(entry.MaxHealth, entry.CurrentHealth, entry.BodyArmor, entry.HeadArmor);
    }

    private void Client_ApplyPendingHealth(AISyncEntry entry)
    {
        if (!_clientPendingHealth.TryGetValue(entry.Id, out var pending)) return;
        _clientPendingHealth.Remove(entry.Id);
        Client_ApplyHealthBroadcast(entry, pending.Message, pending.Order);
    }

    public void Server_HandleHealthReport(RpcContext context, AIHealthReportRpc message)
    {
        if (!IsServer || context.Sender == null || CheckAndHandleSyncBlock()) return;
        // Clients request hits. Their predicted HP, armor and death are never authoritative.
        if (!message.HasDamage || message.Damage.DamageValue < 0f ||
            float.IsNaN(message.Damage.DamageValue) || float.IsInfinity(message.Damage.DamageValue)) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null || entry.Status == AIStatus.Dead) return;
        Server_ApplyAiDamage(context, entry, message.Damage).Forget();
    }

    private async UniTaskVoid Server_ApplyAiDamage(RpcContext context, AISyncEntry entry, DamageForwardPayload payload)
    {
        var attacker = ResolveAttacker(context);
        var controller = entry.Controller;
        var character = controller ? controller.CharacterMainControl : null;
        var health = character ? character.Health : null;
        if (!attacker || !health || health.IsDead) return;

        var buff = payload.BuffId != 0 ? await COOPManager.ResolveBuffAsync(payload.WeaponItemId, payload.BuffId) : null;
        if (!attacker || !health || health.IsDead ||
            !CoopSyncDatabase.AI.TryGet(entry.Id, out var currentEntry) || !ReferenceEquals(currentEntry, entry)) return;

        var info = payload.ToDamageInfo(attacker, character.mainDamageReceiver, buff);
        var resolvedDamage = info;
        var processors = FinalDamageProcessors?.GetValue(health) as List<Func<float, float>>;
        var confirmedDamage = 0f;
        void CaptureDamage(DamageInfo value)
        {
            resolvedDamage = value;
            if (processors == null) confirmedDamage = value.finalDamage;
        }
        float CaptureFinalDamage(float value)
        {
            confirmedDamage = value;
            return value;
        }
        var before = health.CurrentHealth;
        var previousHostHurt = IsHostHurt;
        bool applied;
        try
        {
            IsHostHurt = true;
            health.OnHurtEvent.AddListener(CaptureDamage);
            // Observe the native result before its overkill cap, without changing the calculation.
            processors?.Add(CaptureFinalDamage);
            applied = health.Hurt(info);
        }
        finally
        {
            health.OnHurtEvent.RemoveListener(CaptureDamage);
            processors?.Remove(CaptureFinalDamage);
            IsHostHurt = previousHostHurt;
        }
        if (!applied) return;

        var (max, current, body, head) = ReadHealthSafe(health);
        if (max > 0f) entry.MaxHealth = max;
        entry.CurrentHealth = current;
        entry.BodyArmor = body;
        entry.HeadArmor = head;
        HealthSyncDiagnostics.RecordAiHit(entry.Id, payload.DamageValue, before, current);

        if (health.IsDead && entry.Status != AIStatus.Dead)
            Server_HandleDeath(entry, max, current, body, head, resolvedDamage);
        QueueHealthBroadcast(entry, payload, health.IsDead);
        var confirmed = new AIHealthBroadcastRpc
        {
            Id = entry.Id,
            MaxHealth = max,
            CurrentHealth = current,
            BodyArmor = body,
            HeadArmor = head,
            IsDead = health.IsDead,
            HasDamage = true,
            Damage = DamageForwardPayload.FromDamageInfo(resolvedDamage),
            ConfirmedDamage = confirmedDamage
        };
        CoopTool.SendRpcTo(context.Sender, in confirmed);
    }
}
