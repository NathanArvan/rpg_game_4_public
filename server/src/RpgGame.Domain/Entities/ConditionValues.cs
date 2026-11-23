namespace RpgGame.Domain.Entities;

/// <summary>
/// A condition definition (3B.4, docs/roadmap/SPRINT_3B4.md), as loaded from <c>Conditions/conditions.json</c>.
/// Copied into each ability that applies it (<see cref="AbilityEffectValues.Condition"/>) and into each
/// active condition in Redis, so retuning the file never changes a running encounter.
/// </summary>
public class ConditionValues
{
    /// <summary>Catalog key, e.g. "marked".</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>A debuff: resistible, shown in red, and the enemy AI won't stack it.</summary>
    public bool Harmful { get; set; }
    /// <summary>Tags of its periodic damage (e.g. "flame"), for damage-taken filters.</summary>
    public List<string> Tags { get; set; } = [];
    public ModifierValues Modifiers { get; set; } = new();

    public ConditionValues Copy() => new()
    {
        Id = Id, Name = Name, Description = Description, Harmful = Harmful, Tags = [.. Tags], Modifiers = Modifiers.Copy(),
    };
}

/// <summary>What a condition changes (SPRINT_3B4 C3). Every field is optional; zero / null means "no change".</summary>
public class ModifierValues
{
    public int Accuracy { get; set; }
    public int Dodge { get; set; }
    /// <summary>"any" (default), "melee" or "ranged": which attacks <see cref="Accuracy"/> and <see cref="Dodge"/> apply to.</summary>
    public string AttackKind { get; set; } = "any";
    public int DamageDealt { get; set; }
    public DamageTakenValues? DamageTaken { get; set; }
    public int Movement { get; set; }
    public int Ap { get; set; }
    public PeriodicValues? Periodic { get; set; }
    /// <summary>"all" and/or "move".</summary>
    public List<string> Blocks { get; set; } = [];

    public ModifierValues Copy() => new()
    {
        Accuracy = Accuracy, Dodge = Dodge, AttackKind = AttackKind, DamageDealt = DamageDealt,
        DamageTaken = DamageTaken?.Copy(), Movement = Movement, Ap = Ap, Periodic = Periodic?.Copy(), Blocks = [.. Blocks],
    };
}

public class DamageTakenValues
{
    /// <summary>−50 = resistant, +50 = vulnerable, −100 = immune.</summary>
    public int Percent { get; set; }
    public int Flat { get; set; }
    /// <summary>Only damage from a source with one of these tags; empty = all damage.</summary>
    public List<string> Tags { get; set; } = [];

    public DamageTakenValues Copy() => new() { Percent = Percent, Flat = Flat, Tags = [.. Tags] };
}

public class PeriodicValues
{
    /// <summary>"damage" or "heal".</summary>
    public string Kind { get; set; } = "damage";
    /// <summary>A die like "d4", or null for none (only the bonus).</summary>
    public string? Die { get; set; }
    public int Bonus { get; set; }

    public PeriodicValues Copy() => new() { Kind = Kind, Die = Die, Bonus = Bonus };
}

/// <summary>
/// A condition on a combatant, as stored in Redis (<c>encounter:{encounterId}:conditions</c>): its definition
/// and the full rounds it lasts after the current one (0 = until the end of this round).
/// </summary>
public class ActiveConditionValues
{
    public ConditionValues Condition { get; set; } = new();
    public int RoundsLeft { get; set; }
    /// <summary>Combatant id of whoever applied it.</summary>
    public string? SourceId { get; set; }
}
