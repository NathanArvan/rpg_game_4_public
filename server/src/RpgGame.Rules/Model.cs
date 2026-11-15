namespace RpgGame.Rules;

public readonly record struct GridPosition(int X, int Y)
{
    public static readonly GridPosition Origin = new(0, 0);

    /// <summary>Chebyshev (king-move) distance: diagonal steps cost the same as cardinal ones.</summary>
    public int ChebyshevDistanceTo(GridPosition other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>A single damage die, e.g. d8. Rolled once per hit (1dN).</summary>
public readonly record struct DamageDie(int Sides)
{
    public static readonly DamageDie D4 = new(4);
    public static readonly DamageDie D6 = new(6);
    public static readonly DamageDie D8 = new(8);

    /// <summary>Parses "d8" or "1d8".</summary>
    public static DamageDie Parse(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.StartsWith("1d")) t = t[1..];
        if (t.StartsWith('d') && int.TryParse(t[1..], out var sides) && sides > 0)
            return new DamageDie(sides);
        throw new FormatException($"Invalid damage die '{text}'. Expected e.g. \"d8\" or \"1d8\".");
    }

    public override string ToString() => $"1d{Sides}";
}

public enum ActionType
{
    Move,
    MeleeAttack,
    RangedAttack,
    Defend,
}

/// <summary>
/// A combatant's derived combat values (roadmap 3B.1). Hand-authored per preset until the stats
/// decision; the rules engine only ever reads these, never where they came from.
/// </summary>
/// <param name="MaxHp">Maximum HP.</param>
/// <param name="MovementRange">Max cells per move (Chebyshev steps).</param>
/// <param name="Speed">Movement-conflict tiebreaker: the higher Speed takes a contested cell.</param>
/// <param name="MeleeAccuracy">Added to the d20 of this combatant's melee attacks.</param>
/// <param name="RangedAccuracy">Added to the d20 of this combatant's ranged attacks.</param>
/// <param name="Dodge">An attack against this combatant hits when d20 + accuracy is at least this.</param>
/// <param name="BaseDamage">Damage die rolled once per hit.</param>
/// <param name="MaxAp">
/// Action points to spend each round (roadmap 3B.2). Resets every round; costs come from
/// <see cref="RulesConfig.ApCost"/>.
/// </param>
public sealed record CombatProfile(
    int MaxHp,
    int MovementRange,
    int Speed,
    int MeleeAccuracy,
    int RangedAccuracy,
    int Dodge,
    DamageDie BaseDamage,
    int MaxAp = CombatProfile.DefaultMaxAp)
{
    public const int DefaultMaxAp = 4;

    /// <summary>The placeholder values: 20 HP, 6 cells, Speed 10, +0 accuracy, Dodge 10, d8, 4 AP.</summary>
    public static CombatProfile Default { get; } = new(20, 6, 10, 0, 0, 10, DamageDie.D8);
}

/// <param name="Position">
/// Current cell, or null if the combatant has no map position. Such a combatant isn't on the map:
/// it can't move, can't make or receive melee attacks, and doesn't block anyone.
/// </param>
public sealed record CombatantState(
    string Id,
    string Name,
    int Hp,
    CombatProfile Profile,
    GridPosition? Position,
    bool IsDefeated)
{
    public int MaxHp => Profile.MaxHp;
    public int Speed => Profile.Speed;
}

public sealed record EncounterState(
    int GridWidth,
    int GridHeight,
    IReadOnlyList<CombatantState> Combatants);

/// <param name="TargetCell">Set for <see cref="ActionType.Move"/>.</param>
/// <param name="TargetCombatantId">Set for attacks.</param>
public sealed record PlannedAction(
    ActionType Type,
    GridPosition? TargetCell = null,
    string? TargetCombatantId = null)
{
    public static PlannedAction Move(int x, int y) => new(ActionType.Move, new GridPosition(x, y));
    public static PlannedAction Melee(string targetId) => new(ActionType.MeleeAttack, TargetCombatantId: targetId);
    public static PlannedAction Ranged(string targetId) => new(ActionType.RangedAttack, TargetCombatantId: targetId);
    public static PlannedAction Defend() => new(ActionType.Defend);
}
