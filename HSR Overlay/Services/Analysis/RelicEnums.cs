using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public enum RelicSet
{
    None = 0,
    Genius
}

public enum RelicSlot
{
    None,
    Head,
    Hands,
    Body,
    Feet,
    Sphere,
    Rope
}

public enum Stat
{
    None,
    HpFlat,
    HpPercent,
    AtkFlat,
    AtkPercent,
    DefFlat,
    DefPercent,
    CritRate,
    CritDamage,
    Speed,
    BreakEffect,
    EnergyRegen,
    EffectHitRate,
    EffectRes
}