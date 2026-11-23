namespace RpgGame.Api.Contracts.Hub;

/// <summary>One ability of a combatant's loadout, as the client needs it to plan (3B.3).</summary>
/// <param name="ApCost">AP the ability costs when queued.</param>
/// <param name="Cooldown">Rounds it can't be planned after the round it was used.</param>
/// <param name="Move">How the ability moves its caster (Dash, Charge), or null if it doesn't.</param>
/// <param name="AppliesConditions">The conditions it applies (3B.4).</param>
public record AbilityDto(
    string Id,
    string Name,
    string Description,
    int ApCost,
    int Cooldown,
    IReadOnlyList<string> Tags,
    AbilityTargetingDto Targeting,
    AbilityMoveDto? Move = null,
    IReadOnlyList<AbilityConditionDto>? AppliesConditions = null);

/// <summary>An ability's move effect (3B.3).</summary>
/// <param name="To">"cell" (planned with a target cell) or "target" (to the free cell next to the ability's target closest to the caster).</param>
/// <param name="MaxCells">Most cells it covers.</param>
/// <param name="UsesMovementBudget">The cells come out of the round's movement range; false = on top of it (Dash).</param>
public record AbilityMoveDto(
    string To,
    int MaxCells,
    bool UsesMovementBudget);

/// <param name="Kind">"self" (the caster only), "combatant" (one chosen target), "combatants" (up to <paramref name="Count"/> chosen) or "auto" (found when it resolves).</param>
/// <param name="Filter">"enemy", "ally", "any" or "downed_ally".</param>
/// <param name="Range">Chebyshev cells from the caster when it resolves (for an ability that moves: when the move starts). 0 for "self".</param>
/// <param name="Count">"combatants": max chosen targets. "auto": max hit (null = all).</param>
/// <param name="IncludeSelf">Ally abilities: the caster can target itself.</param>
public record AbilityTargetingDto(
    string Kind,
    string Filter,
    int Range,
    int? Count,
    bool IncludeSelf);

/// <summary>
/// One ability activation in a tick (3B.3). Its attacks are in the tick's attack list with the same
/// <see cref="AbilityId"/>, its heals in the tick's heal list. <see cref="WastedReason"/> is set
/// only when the activation itself failed: refused ("on_cooldown", "not_in_loadout"), or an "auto"
/// ability that found nobody ("no_targets", which still starts the cooldown). For an "auto"
/// ability, <see cref="TargetIds"/> are the targets it found.
/// </summary>
public record AbilityUseResult(
    string CombatantId,
    string CombatantName,
    string AbilityId,
    string AbilityName,
    IReadOnlyList<string> TargetIds,
    string? WastedReason,
    string FeedText);
