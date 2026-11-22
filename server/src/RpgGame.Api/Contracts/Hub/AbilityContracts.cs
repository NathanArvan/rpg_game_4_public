namespace RpgGame.Api.Contracts.Hub;

/// <summary>One ability of a combatant's loadout, as the client needs it to plan (3B.3).</summary>
/// <param name="ApCost">AP the ability costs when queued.</param>
/// <param name="Cooldown">Rounds it can't be planned after the round it was used.</param>
public record AbilityDto(
    string Id,
    string Name,
    string Description,
    int ApCost,
    int Cooldown,
    IReadOnlyList<string> Tags,
    AbilityTargetingDto Targeting);

/// <param name="Kind">"combatant" (one chosen target), "combatants" (up to <paramref name="Count"/> chosen) or "auto" (found when it resolves); "self" later in 3B.3.</param>
/// <param name="Filter">"enemy", "ally", "any" or "downed_ally".</param>
/// <param name="Range">Chebyshev cells from the caster when it resolves.</param>
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
