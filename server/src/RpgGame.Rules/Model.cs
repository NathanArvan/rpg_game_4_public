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
    /// <summary>Stops an adjacent Downed combatant bleeding out (3B.5). Resolves in the Effects phase.</summary>
    Stabilize,
    /// <summary>
    /// Uses one of the combatant's abilities (3B.3, <see cref="PlannedAction.AbilityId"/>). Takes one
    /// queue slot; each of its effects resolves in its own phase of that tick.
    /// </summary>
    Ability,
}

/// <summary>
/// Where a combatant stands in the fight (3B.5, docs/design/DEFEAT_AND_INCAPACITATION.md).
/// Anyone not <see cref="Standing"/> is out of the fight: they can't act.
/// </summary>
public enum CombatantStatus
{
    Standing,
    /// <summary>At 0 HP and bleeding out: dies when <see cref="CombatantState.BleedoutRoundsLeft"/> runs out.</summary>
    Downed,
    /// <summary>At 0 HP but stabilized: out of this fight, no longer dying.</summary>
    Stable,
    Dead,
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
/// <param name="CanBeDowned">
/// At 0 HP this combatant goes Downed (and can be stabilized) instead of dying outright (3B.5).
/// True for party profiles; monsters die at 0 HP unless a preset says otherwise.
/// </param>
public sealed record CombatProfile(
    int MaxHp,
    int MovementRange,
    int Speed,
    int MeleeAccuracy,
    int RangedAccuracy,
    int Dodge,
    DamageDie BaseDamage,
    int MaxAp = CombatProfile.DefaultMaxAp,
    bool CanBeDowned = false)
{
    public const int DefaultMaxAp = 4;

    /// <summary>
    /// The combatant's ability loadout (3B.3): full definitions, copied in when the combatant was
    /// created, so the resolver never needs a catalog.
    /// </summary>
    public IReadOnlyList<AbilityDefinition> Abilities { get; init; } = [];

    /// <summary>The loadout's ability with this id, or null.</summary>
    public AbilityDefinition? FindAbility(string? id) =>
        id is null ? null : Abilities.FirstOrDefault(a => a.Id == id);

    // Records compare collections by reference; the loadout is compared by content.
    public bool Equals(CombatProfile? other) =>
        other is not null
        && MaxHp == other.MaxHp && MovementRange == other.MovementRange && Speed == other.Speed
        && MeleeAccuracy == other.MeleeAccuracy && RangedAccuracy == other.RangedAccuracy
        && Dodge == other.Dodge && BaseDamage == other.BaseDamage && MaxAp == other.MaxAp
        && CanBeDowned == other.CanBeDowned
        && Abilities.SequenceEqual(other.Abilities);

    public override int GetHashCode() =>
        HashCode.Combine(MaxHp, MovementRange, Speed, MeleeAccuracy, RangedAccuracy, Dodge, BaseDamage, HashCode.Combine(MaxAp, CanBeDowned, Abilities.Count));

    /// <summary>The placeholder values: 20 HP, 6 cells, Speed 10, +0 accuracy, Dodge 10, d8, 4 AP.</summary>
    public static CombatProfile Default { get; } = new(20, 6, 10, 0, 0, 10, DamageDie.D8);
}

/// <param name="Position">
/// Current cell, or null if the combatant has no map position. Such a combatant isn't on the map:
/// it can't move, can't make or receive melee attacks, and doesn't block anyone.
/// </param>
/// <param name="Status">Standing, Downed, Stable or Dead (3B.5).</param>
/// <param name="BleedoutRoundsLeft">While Downed: end-of-round countdowns left before dying. 0 otherwise.</param>
public sealed record CombatantState(
    string Id,
    string Name,
    int Hp,
    CombatProfile Profile,
    GridPosition? Position,
    CombatantStatus Status = CombatantStatus.Standing,
    int BleedoutRoundsLeft = 0)
{
    public int MaxHp => Profile.MaxHp;
    public int Speed => Profile.Speed;

    /// <summary>
    /// Team name (3B.3). Who is an ally or an enemy is decided only by <see cref="Relations.Between"/>.
    /// Empty: an enemy of everyone. A free string, so any number of teams works.
    /// </summary>
    public string Team { get; init; } = "";

    /// <summary>Abilities on cooldown: ability id → rounds left before it can be planned again (3B.3).</summary>
    public IReadOnlyDictionary<string, int> Cooldowns { get; init; } = EmptyCooldowns;

    /// <summary>Rounds before <paramref name="abilityId"/> can be planned again; 0 = ready.</summary>
    public int CooldownLeft(string abilityId) => Cooldowns.GetValueOrDefault(abilityId);

    private static readonly IReadOnlyDictionary<string, int> EmptyCooldowns = new Dictionary<string, int>();

    // Records compare collections by reference; cooldowns are compared by content so two states
    // with the same values are equal (determinism tests, scenario checks).
    public bool Equals(CombatantState? other) =>
        other is not null
        && Id == other.Id && Name == other.Name && Hp == other.Hp
        && Equals(Profile, other.Profile) && Position == other.Position
        && Status == other.Status && BleedoutRoundsLeft == other.BleedoutRoundsLeft
        && Team == other.Team
        && Cooldowns.Count == other.Cooldowns.Count
        && Cooldowns.All(kv => other.Cooldowns.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public override int GetHashCode() => HashCode.Combine(Id, Hp, Position, Status, BleedoutRoundsLeft, Team, Cooldowns.Count);

    /// <summary>Out of the fight: Downed, Stable or Dead. Such a combatant can't act.</summary>
    public bool IsDefeated => Status != CombatantStatus.Standing;
}

public sealed record EncounterState(
    int GridWidth,
    int GridHeight,
    IReadOnlyList<CombatantState> Combatants);

/// <param name="TargetCell">Set for <see cref="ActionType.Move"/>.</param>
/// <param name="TargetCombatantId">Set for attacks, Stabilize and single-target abilities.</param>
/// <param name="AbilityId">Set for <see cref="ActionType.Ability"/>: which loadout ability.</param>
/// <param name="TargetCombatantIds">Abilities that pick several targets (3B.3 PR 2).</param>
public sealed record PlannedAction(
    ActionType Type,
    GridPosition? TargetCell = null,
    string? TargetCombatantId = null,
    string? AbilityId = null,
    IReadOnlyList<string>? TargetCombatantIds = null)
{
    public static PlannedAction Move(int x, int y) => new(ActionType.Move, new GridPosition(x, y));
    public static PlannedAction Melee(string targetId) => new(ActionType.MeleeAttack, TargetCombatantId: targetId);
    public static PlannedAction Ranged(string targetId) => new(ActionType.RangedAttack, TargetCombatantId: targetId);
    public static PlannedAction Defend() => new(ActionType.Defend);
    public static PlannedAction Stabilize(string targetId) => new(ActionType.Stabilize, TargetCombatantId: targetId);
    public static PlannedAction Ability(string abilityId, string? targetId = null) =>
        new(ActionType.Ability, TargetCombatantId: targetId, AbilityId: abilityId);
}
