using System.Reflection;
using Duckov.Utilities;
using ItemStatsSystem;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class PickupThrowableSync
{
    internal sealed class Capture
    {
        internal Capture Previous;
        internal Grenade Grenade;
        internal Vector3 Position, Velocity;
    }

    [ThreadStatic] private static Capture _capture;
    private static readonly FieldInfo BoundAgent = AccessTools.Field(typeof(Grenade), "bindedAgent");

    internal static Skill_Grenade GetSkill(Item item) => item ?
        item.GetComponent<ItemSetting_Skill>()?.Skill as Skill_Grenade ?? item.GetComponent<Skill_Grenade>() : null;

    internal static Capture Begin(Skill_Grenade skill)
    {
        if (!skill.createPickup || !skill.fromItem || NetService.Instance?.networkStarted != true) return null;
        var capture = new Capture { Previous = _capture };
        _capture = capture;
        return capture;
    }

    internal static bool Launched(Grenade grenade, Vector3 position, Vector3 velocity)
    {
        if (grenade.GetComponent<PickupThrowableTag>()) return true;
        if (_capture == null || _capture.Grenade) return false;
        _capture.Grenade = grenade;
        _capture.Position = position;
        _capture.Velocity = velocity;
        return true;
    }

    internal static void End(Capture capture, bool succeeded)
    {
        if (capture == null) return;
        _capture = capture.Previous;
        var grenade = capture.Grenade;
        if (!succeeded || !grenade) return;
        var agent = BoundAgent.GetValue(grenade) as DuckovItemAgent;
        var item = agent ? agent.Item : null;
        if (!item) return;
        grenade.gameObject.AddComponent<PickupThrowableTag>();
        if (NetService.Instance.IsServer)
            COOPManager.ItemNet.Server_RegisterPickupThrowable(item, agent, capture.Position, capture.Velocity);
        else
        {
            COOPManager.ItemNet.Client_RequestPickupThrowable(item, capture.Position, capture.Velocity);
            Object.Destroy(grenade.gameObject);
        }
    }

    internal static DuckovItemAgent Spawn(Item item, Vector3 position, Vector3 velocity, CharacterMainControl owner = null)
    {
        var skill = GetSkill(item);
        if (!skill || !skill.createPickup || !skill.grenadePfb) return null;
        var grenade = Object.Instantiate(skill.grenadePfb, position, Quaternion.identity);
        grenade.gameObject.AddComponent<PickupThrowableTag>();
        grenade.damageInfo = skill.damageInfo;
        grenade.damageInfo.fromCharacter = owner;
        grenade.createExplosion = skill.createExplosion;
        grenade.explosionShakeStrength = skill.explosionShakeStrength;
        grenade.damageRange = skill.SkillContext.effectRange;
        grenade.delayFromCollide = skill.delayFromCollide;
        grenade.delayTime = skill.delay;
        grenade.isLandmine = skill.isLandmine;
        grenade.landmineTriggerRange = skill.landmineTriggerRange;
        grenade.SetWeaponIdInfo(item.TypeID);
        grenade.Launch(position, velocity, owner, skill.canHurtSelf);
        item.Detach();
        item.AgentUtilities.ReleaseActiveAgent();
        var agent = item.AgentUtilities.CreateAgent(GameplayDataSettings.Prefabs.PickupAgentNoRendererPrefab, ItemAgent.AgentTypes.pickUp);
        grenade.BindAgent(agent);
        return agent as DuckovItemAgent;
    }
}

internal sealed class PickupThrowableTag : MonoBehaviour { }
