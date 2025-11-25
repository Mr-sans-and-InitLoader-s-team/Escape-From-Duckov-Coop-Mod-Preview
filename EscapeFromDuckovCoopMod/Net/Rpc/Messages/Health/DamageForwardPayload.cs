using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public struct DamageForwardPayload
{
    public float DamageValue;
    public float ArmorPiercing;
    public float CritDamageFactor;
    public float CritRate;
    public int Crit;
    public Vector3 HitPoint;
    public Vector3 HitNormal;
    public int WeaponItemId;
    public float BleedChance;
    public bool IsExplosion;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(DamageValue);
        writer.Put(ArmorPiercing);
        writer.Put(CritDamageFactor);
        writer.Put(CritRate);
        writer.Put(Crit);
        writer.PutV3cm(HitPoint);
        writer.PutDir(HitNormal);
        writer.Put(WeaponItemId);
        writer.Put(BleedChance);
        writer.Put(IsExplosion);
    }

    public void Deserialize(NetPacketReader reader)
    {
        DamageValue = reader.GetFloat();
        ArmorPiercing = reader.GetFloat();
        CritDamageFactor = reader.GetFloat();
        CritRate = reader.GetFloat();
        Crit = reader.GetInt();
        HitPoint = reader.GetV3cm();
        HitNormal = reader.GetDir();
        WeaponItemId = reader.GetInt();
        BleedChance = reader.GetFloat();
        IsExplosion = reader.GetBool();
    }

    public static DamageForwardPayload FromDamageInfo(DamageInfo? di)
    {
        if (!di.HasValue)
            return default;

        var value = di.Value;
        return new DamageForwardPayload
        {
            DamageValue = value.damageValue,
            ArmorPiercing = value.armorPiercing,
            CritDamageFactor = value.critDamageFactor,
            CritRate = value.critRate,
            Crit = value.crit,
            HitPoint = value.damagePoint,
            HitNormal = value.damageNormal.sqrMagnitude < 1e-6f ? Vector3.forward : value.damageNormal.normalized,
            WeaponItemId = value.fromWeaponItemID,
            BleedChance = value.bleedChance,
            IsExplosion = value.isExplosion
        };
    }

    public DamageInfo ToDamageInfo(CharacterMainControl attacker = null, DamageReceiver target = null)
    {
        var info = new DamageInfo(attacker)
        {
            damageValue = DamageValue,
            armorPiercing = ArmorPiercing,
            critDamageFactor = CritDamageFactor,
            critRate = CritRate,
            crit = Crit,
            damagePoint = HitPoint,
            damageNormal = HitNormal.sqrMagnitude < 1e-6f ? Vector3.forward : HitNormal.normalized,
            fromWeaponItemID = WeaponItemId,
            bleedChance = BleedChance,
            isExplosion = IsExplosion
        };

        if (target != null)
            info.toDamageReceiver = target;

        return info;
    }
}
