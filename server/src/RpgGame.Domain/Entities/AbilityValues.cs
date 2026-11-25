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
    /// <summary>"active" (resolves at its queue position) or "prepared" (a reaction that watches for its trigger, 3B.6).</summary>
    public string Activation { get; set; } = "active";
    /// <summary>What sets off a prepared ability (3B.6); null for an active one.</summary>
    public AbilityTriggerValues? Trigger { get; set; }

    public AbilityValues Copy() => new()
    {
        Activation = Activation,
        Trigger = Trigger?.Copy(),
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

/// <summary>The trigger of a prepared ability (3B.6, SPRINT_3B6 X6).</summary>
public class AbilityTriggerValues
{
    /// <summary>"enemy_enters_reach", "enemy_moves_within", "attacked" or "ally_attacked".</summary>
    public string Kind { get; set; } = "";
    /// <summary>Cells from the watcher ("enemy_moves_within", "ally_attacked").</summary>
    public int? Range { get; set; }
    /// <summary>"attacked": only "melee" or "ranged" attacks; null for any.</summary>
    public string? AttackKind { get; set; }

    public AbilityTriggerValues Copy() => new() { Kind = Kind, Range = Range, AttackKind = AttackKind };
}

public class AbilityTargetValues
{
    /// <summary>"self", "combatant", "combatants", "auto", "trigger" (prepared reactions, 3B.6) (and "cell", reserved).</summary>
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
    /// <summary>"attack", "heal" or "move" (3B.3), "apply_condition" (3B.4), "modify_incoming" or "redirect" (prepared reactions, 3B.6).</summary>
    public string Kind { get; set; } = "";
    /// <summary>Attack: "melee", "ranged" or "none" (always hits).</summary>
    public string? Roll { get; set; }
    public int AccuracyBonus { get; set; }
    public AbilityDamageValues? Damage { get; set; }
    /// <summary>Heal: HP restored (die + bonus).</summary>
    public AbilityAmountValues? Amount { get; set; }
    /// <summary>Heal: brings a downed or stable target back on their feet.</summary>
    public bool CanRevive { get; set; } = true;
    /// <summary>Move: "cell" (a planned cell) or "target" (next to the ability's target).</summary>
    public string? To { get; set; }
    /// <summary>Move: most cells it covers.</summary>
    public int MaxCells { get; set; }
    /// <summary>Move: the cells come out of the round's movement budget (false: on top of it, like Dash).</summary>
    public bool UsesMovementBudget { get; set; } = true;
    /// <summary>Apply condition (3B.4): the full condition definition, copied in when the ability was loaded.</summary>
    public ConditionValues? Condition { get; set; }
    /// <summary>Apply condition: full rounds it lasts after the current one (0 = until the end of this round).</summary>
    public int Duration { get; set; }
    /// <summary>Apply condition: only targets the same ability's attack hit get it.</summary>
    public bool RequiresHit { get; set; }
    /// <summary>Modify incoming (3B.6 PR 3, Parry): Dodge added against the attacks that set the reaction off.</summary>
    public int Dodge { get; set; }

    public AbilityEffectValues Copy() => new()
    {
        Kind = Kind, Roll = Roll, AccuracyBonus = AccuracyBonus, Damage = Damage?.Copy(), Amount = Amount?.Copy(),
        CanRevive = CanRevive, To = To, MaxCells = MaxCells, UsesMovementBudget = UsesMovementBudget,
        Condition = Condition?.Copy(), Duration = Duration, RequiresHit = RequiresHit, Dodge = Dodge,
    };
}

public class AbilityAmountValues
{
    /// <summary>A die like "d6", or null for none (only the bonus).</summary>
    public string? Die { get; set; }
    public int Bonus { get; set; }

    public AbilityAmountValues Copy() => new() { Die = Die, Bonus = Bonus };
}

public class AbilityDamageValues
{
    /// <summary>"weapon" (the caster's own damage die) or a die like "d10".</summary>
    public string Die { get; set; } = "weapon";
    public int Bonus { get; set; }
    public int Multiplier { get; set; } = 1;
    /// <summary>Extra damage when the ability's move covered at least <c>MinCells</c> cells (Charge).</summary>
    public AbilityMovedBonusValues? BonusIfMoved { get; set; }

    public AbilityDamageValues Copy() => new() { Die = Die, Bonus = Bonus, Multiplier = Multiplier, BonusIfMoved = BonusIfMoved?.Copy() };
}

public class AbilityMovedBonusValues
{
    public int MinCells { get; set; }
    public int Bonus { get; set; }

    public AbilityMovedBonusValues Copy() => new() { MinCells = MinCells, Bonus = Bonus };
}
