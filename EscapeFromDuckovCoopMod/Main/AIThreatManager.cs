using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

internal static class AIThreatManager
{
    private sealed class ThreatEntry
    {
        public DamageReceiver Target;
        public float AccumulatedDamage;
        public float LastUpdateTime;
    }

    private static readonly Dictionary<AICharacterController, Dictionary<DamageReceiver, ThreatEntry>> Threats = new();

    private static bool ShouldRun()
    {
        var svc = NetService.Instance;
        return svc != null && svc.networkStarted && svc.IsServer;
    }

    public static void Clear(AICharacterController controller)
    {
        if (controller == null) return;
        Threats.Remove(controller);
    }

    public static void NoteHit(AICharacterController controller, DamageInfo damage)
    {
        if (!ShouldRun() || controller == null) return;
        if (damage.fromCharacter == null) return;

        var target = damage.fromCharacter.mainDamageReceiver;
        if (target == null || target.Team == controller.CharacterMainControl.Team || IsTargetDead(target)) return;

        if (!Threats.TryGetValue(controller, out var entries))
        {
            entries = new Dictionary<DamageReceiver, ThreatEntry>();
            Threats[controller] = entries;
        }

        if (!entries.TryGetValue(target, out var entry))
        {
            entry = new ThreatEntry { Target = target, AccumulatedDamage = 0f, LastUpdateTime = Time.time };
            entries[target] = entry;
        }

        entry.AccumulatedDamage = Mathf.Min(entry.AccumulatedDamage + damage.finalDamage, 9999f);
        entry.LastUpdateTime = Time.time;
    }

    public static void Evaluate(AICharacterController controller)
    {
        if (!ShouldRun() || controller == null) return;
        if (!Threats.TryGetValue(controller, out var entries) || entries.Count == 0) return;

        var now = Time.time;
        entries
            .Where(kv => kv.Value == null || IsTargetDead(kv.Key) || now - kv.Value.LastUpdateTime > 20f)
            .Select(kv => kv.Key)
            .ToList()
            .ForEach(key => entries.Remove(key));

        var cmc = controller.CharacterMainControl;
        var maxVisionDistance = cmc != null ? cmc.ViewDistance : 0f;

        var current = controller.searchedEnemy;
        if (IsTargetDead(current))
        {
            controller.SetNoticedToTarget(null);
            controller.searchedEnemy = null;
            current = null;
        }
        else if (current != null && maxVisionDistance > 0f)
        {
            var distanceToCurrent = Vector3.Distance(controller.transform.position, current.transform.position);
            if (distanceToCurrent > maxVisionDistance)
            {
                controller.SetNoticedToTarget(null);
                controller.searchedEnemy = null;
                current = null;
            }
        }

        if (entries.Count == 0) return;

        var currentScore = current && entries.TryGetValue(current, out var currentEntry)
            ? Score(controller, now, maxVisionDistance, currentEntry)
            : -1f;
        var bestTarget = current;
        var bestScore = currentScore;

        foreach (var entry in entries.Values)
        {
            var target = entry.Target;
            if (IsTargetDead(target)) continue;

            var score = Score(controller, now, maxVisionDistance, entry);
            if (target == current)
                score += 5f; // maintain some stickiness but allow meaningful overrides

            if (score > bestScore + 0.5f)
            {
                bestScore = score;
                bestTarget = target;
            }
        }

        if (bestTarget != null && bestTarget != current)
        {
            controller.SetNoticedToTarget(bestTarget);
            controller.searchedEnemy = bestTarget;
        }
    }

    private static float Score(AICharacterController controller, float now, float maxVisionDistance, ThreatEntry entry)
    {
        if (entry == null || entry.Target == null) return -1f;

        var distance = Vector3.Distance(controller.transform.position, entry.Target.transform.position);
        if (maxVisionDistance > 0f && distance > maxVisionDistance) return -1f;
        var distanceNormalized = Mathf.Clamp01((40f - distance) / 40f);
        var closeRangeBonus = Mathf.Max(0f, 16f - distance) * 0.6f; // still prefer nearby targets but reduce influence
        var distanceScore = distanceNormalized * 18f + closeRangeBonus; // distance should guide tie-breaks instead of overriding damage

        var age = Mathf.Max(0f, now - entry.LastUpdateTime);
        var decay = Mathf.Exp(-age / 5f); // stale hits fade faster so fresh attackers bubble up
        var cooledDamage = entry.AccumulatedDamage * Mathf.Exp(-age / 14f); // bleed off old burst damage to prevent long-term tunnel vision
        var damageScore = Mathf.Log10(1f + cooledDamage) * 85f * decay; // emphasize sustained, recent damage

        var recencyBonus = Mathf.Lerp(20f, 0f, Mathf.Clamp01(age / 5f)); // sharp boost for the most recent attacker

        return damageScore + distanceScore + recencyBonus;
    }

    private static bool IsTargetDead(DamageReceiver target)
    {
        if (target == null || target.IsDead) return true;

        var health = target.health;
        if (health != null)
        {
            if (health.CurrentHealth <= 0.0001f || health.IsDead) return true;

            var cmc = health.TryGetCharacter();
            if (cmc != null)
            {
                var cmcHealth = cmc.Health;
                if (cmcHealth != null && (cmcHealth.IsDead || cmcHealth.CurrentHealth <= 0.0001f)) return true;

                if (LocalPlayerManager.Instance != null && !LocalPlayerManager.Instance.IsAlive(cmc)) return true;
            }
        }

        return false;
    }
}
