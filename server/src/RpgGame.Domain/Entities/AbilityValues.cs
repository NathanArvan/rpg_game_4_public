namespace RpgGame.Domain.Entities;

/// <summary>
/// One ability of a combatant's loadout (3B.3), copied from the ability catalog when the
/// combatant joins an encounter. Stored inside <see cref="CombatProfileValues"/> (JSON) so a running
/// encounter never depends on the catalog. Same shape as an entry of <c>Abilities/abilities.json</c>.
/// </summary>
public class AbilityValues
{
    /// <summary>Catalog key, e.g. "power_strike".</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int ApCost { get; set; }
    /// <summary>Rounds the ability can't be planned after the round it was used.</summary>
    public int Cooldown { get; set; }
    public List<string> Tags { get; set; } = [];
    public AbilityTargetValues Target { get; set; } = new();
    public List<AbilityEffectValues> Effects { get; set; } = [];

    public AbilityValues Copy() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        ApCost = ApCost,
        Cooldown = Cooldown,
        Tags = [.. Tags],
        Target = Target.Copy(),
        Effects = Effects.Select(e => e.Copy()).ToList(),
    };
}

public class AbilityTargetValues
{
    /// <summary>"self", "combatant", "combatants", "auto" (and "cell", reserved).</summary>
    public string Kind { get; set; } = "combatant";
    /// <summary>"enemy", "ally", "any", "downed_ally".</summary>
    public string Filter { get; set; } = "enemy";
    public int Range { get; set; } = 1;
    public int? Count { get; set; }
    public bool IncludeSelf { get; set; }

    public AbilityTargetValues Copy() => new()
    {
        Kind = Kind, Filter = Filter, Range = Range, Count = Count, IncludeSelf = IncludeSelf,
    };
}

public class AbilityEffectValues
{
    /// <summary>"attack" (3B.3 PR 1); more kinds in later PRs.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Attack: "melee", "ranged" or "none" (always hits).</summary>
    public string? Roll { get; set; }
    public int AccuracyBonus { get; set; }
    public AbilityDamageValues? Damage { get; set; }

    public AbilityEffectValues Copy() => new()
    {
        Kind = Kind, Roll = Roll, AccuracyBonus = AccuracyBonus, Damage = Damage?.Copy(),
    };
}

public class AbilityDamageValues
{
    /// <summary>"weapon" (the caster's own damage die) or a die like "d10".</summary>
    public string Die { get; set; } = "weapon";
    public int Bonus { get; set; }
    public int Multiplier { get; set; } = 1;

    public AbilityDamageValues Copy() => new() { Die = Die, Bonus = Bonus, Multiplier = Multiplier };
}
