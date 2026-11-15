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

/// <param name="Speed">
/// Movement-conflict tiebreaker. Fed from Dex until 3B.1 introduces the real Speed stat.
/// </param>
/// <param name="Position">
/// Current cell, or null if the combatant has no map position. Such a combatant isn't on the map:
/// it can't move, can't make or receive melee attacks, and doesn't block anyone.
/// </param>
public sealed record CombatantState(
    string Id,
    string Name,
    int Hp,
    int MaxHp,
    int Speed,
    DamageDie Damage,
    GridPosition? Position,
    bool IsDefeated);

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
