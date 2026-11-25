namespace RpgGame.Rules;

/// <summary>
/// Tunable rule constants shared by every combatant. Per-combatant values (accuracy, dodge,
/// movement range, AP budget, …) live on <see cref="CombatProfile"/>.
/// </summary>
public sealed record RulesConfig
{
    // Declared before Default: static initializers run in order, and Default reads it.
    /// <summary>The built-in Guarded condition (+3 Dodge).</summary>
    public static ConditionDefinition DefaultGuarded { get; } =
        new(GuardedId, "Guarded", Harmful: false, Tags: [], new Modifiers(Dodge: 3));

    public static RulesConfig Default { get; } = new();

    /// <summary>Max Chebyshev distance for a melee attack, checked after movement.</summary>
    public int MeleeReach { get; init; } = 1;

    /// <summary>AP cost of one Move (roadmap 3B.2). All of a round's moves share the mover's MovementRange cells.</summary>
    public int MoveApCost { get; init; } = 1;

    /// <summary>
    /// AP cost of one Disengage (3B.6, SPRINT_3B6 X4): a move that doesn't provoke opportunity attacks. More
    /// than a Move, so stepping away safely costs something.
    /// </summary>
    public int DisengageApCost { get; init; } = 2;

    /// <summary>AP cost of one melee attack.</summary>
    public int MeleeApCost { get; init; } = 2;

    /// <summary>AP cost of one ranged attack.</summary>
    public int RangedApCost { get; init; } = 2;

    /// <summary>AP cost of one Defend.</summary>
    public int DefendApCost { get; init; } = 1;

    /// <summary>AP cost of one Stabilize (3B.5): the same as an attack, so rescuing competes with attacking.</summary>
    public int StabilizeApCost { get; init; } = 2;

    /// <summary>
    /// Rounds a Downed combatant has before dying: allies get this many planning phases to stabilize
    /// them (the round they fell in doesn't count). Flat for now; to come from a stat such as
    /// Hardiness once character stats exist (DEFEAT_AND_INCAPACITATION.md).
    /// </summary>
    public int BleedoutRounds { get; init; } = 2;

    /// <summary>
    /// The condition Defend grants (3B.4, SPRINT_3B4 C7): +3 Dodge from the start of the Defend's tick until
    /// the end of the round. Built in, so Rules (and scenarios) need no catalog for it.
    /// </summary>
    public ConditionDefinition GuardedCondition { get; init; } = DefaultGuarded;

    /// <summary>Id of <see cref="GuardedCondition"/>.</summary>
    public const string GuardedId = "guarded";

    public int ApCost(ActionType type) => type switch
    {
        ActionType.Move => MoveApCost,
        ActionType.MeleeAttack => MeleeApCost,
        ActionType.RangedAttack => RangedApCost,
        ActionType.Defend => DefendApCost,
        ActionType.Stabilize => StabilizeApCost,
        ActionType.Disengage => DisengageApCost,
        ActionType.Ability => throw new ArgumentException("An ability's cost comes from its definition: use ApCost(action, profile).", nameof(type)),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown action type."),
    };

    /// <summary>
    /// AP cost of one queued action: basic actions from this config, abilities from their definition
    /// in <paramref name="profile"/>'s loadout (3B.3). An ability the profile doesn't have costs 0:
    /// <see cref="PlanValidator"/> rejects it and the resolver wastes it, so it never buys anything.
    /// </summary>
    public int ApCost(PlannedAction action, CombatProfile profile) => action.Type == ActionType.Ability
        ? profile.FindAbility(action.AbilityId)?.ApCost ?? 0
        : ApCost(action.Type);

    /// <summary>Basic action types (everything except <see cref="ActionType.Ability"/>), whose cost is fixed.</summary>
    public static IEnumerable<ActionType> BasicActionTypes =>
        Enum.GetValues<ActionType>().Where(t => t != ActionType.Ability);
}
