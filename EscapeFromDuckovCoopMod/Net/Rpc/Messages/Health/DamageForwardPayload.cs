using System.Collections.Generic;
using Duckov.Buffs;
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
    public int DamageType;
    public bool IsFromBuffOrEffect;
    public float DamageFactorToZombie;
    public bool IgnoreArmor;
    public bool IgnoreDifficulty;
    public float ArmorBreak;
    public float BuffChance;
    public int BuffId;
    public List<ElementFactor> ElementFactors;

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
        writer.Put(DamageType);
        writer.Put(IsFromBuffOrEffect);
        writer.Put(DamageFactorToZombie);
        writer.Put(IgnoreArmor);
        writer.Put(IgnoreDifficulty);
        writer.Put(ArmorBreak);
        writer.Put(BuffChance);
        writer.Put(BuffId);

        var count = Mathf.Min(ElementFactors?.Count ?? 0, 16);
        writer.Put((byte)count);
        for (var i = 0; i < count; i++)
        {
            writer.Put((int)ElementFactors[i].elementType);
            writer.Put(ElementFactors[i].factor);
        }
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
        DamageType = reader.GetInt();
        IsFromBuffOrEffect = reader.GetBool();
        DamageFactorToZombie = reader.GetFloat();
        IgnoreArmor = reader.GetBool();
        IgnoreDifficulty = reader.GetBool();
        ArmorBreak = reader.GetFloat();
        BuffChance = reader.GetFloat();
        BuffId = reader.GetInt();

        var count = reader.GetByte();
        ElementFactors = new List<ElementFactor>(Mathf.Min(count, 16));
        for (var i = 0; i < count; i++)
        {
            var type = (ElementTypes)reader.GetInt();
            var factor = reader.GetFloat();
            if (i < 16)
                ElementFactors.Add(new ElementFactor(type, factor));
        }
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
            IsExplosion = value.isExplosion,
            DamageType = (int)value.damageType,
            IsFromBuffOrEffect = value.isFromBuffOrEffect,
            DamageFactorToZombie = value.damageFactorToZombie,
            IgnoreArmor = value.ignoreArmor,
            IgnoreDifficulty = value.ignoreDifficulty,
            ArmorBreak = value.armorBreak,
            BuffChance = value.buffChance,
            BuffId = value.buff ? value.buff.ID : 0,
            ElementFactors = value.elementFactors == null
                ? new List<ElementFactor>()
                : new List<ElementFactor>(value.elementFactors)
        };
    }

    public DamageInfo ToDamageInfo(
        CharacterMainControl attacker = null,
        DamageReceiver target = null,
        Buff resolvedBuff = null)
    {
        var info = new DamageInfo(attacker)
        {
            damageType = (DamageTypes)DamageType,
            isFromBuffOrEffect = IsFromBuffOrEffect,
            damageValue = DamageValue,
            damageFactorToZombie = DamageFactorToZombie,
            ignoreArmor = IgnoreArmor,
            ignoreDifficulty = IgnoreDifficulty,
            armorPiercing = ArmorPiercing,
            critDamageFactor = CritDamageFactor,
            critRate = CritRate,
            crit = Crit,
            damagePoint = HitPoint,
            damageNormal = HitNormal.sqrMagnitude < 1e-6f ? Vector3.forward : HitNormal.normalized,
            fromWeaponItemID = WeaponItemId,
            armorBreak = ArmorBreak,
            buffChance = BuffChance,
            buff = resolvedBuff,
            bleedChance = BleedChance,
            isExplosion = IsExplosion,
            elementFactors = ElementFactors == null
                ? new List<ElementFactor>()
                : new List<ElementFactor>(ElementFactors)
        };

        if (target != null)
            info.toDamageReceiver = target;

        return info;
    }
}
