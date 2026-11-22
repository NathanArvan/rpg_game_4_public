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

/// <param name="Kind">"combatant" for now ("combatants", "auto", "self" in later 3B.3 PRs).</param>
/// <param name="Filter">"enemy", "ally", "any" or "downed_ally".</param>
/// <param name="Range">Chebyshev cells from the caster when it resolves.</param>
public record AbilityTargetingDto(
    string Kind,
    string Filter,
    int Range,
    int? Count,
    bool IncludeSelf);

/// <summary>
/// One ability activation in a tick (3B.3). Its attacks are in the tick's attack list with the same
/// <see cref="AbilityId"/>. <see cref="WastedReason"/> is set only when the activation itself was
/// refused ("on_cooldown", "not_in_loadout").
/// </summary>
public record AbilityUseResult(
    string CombatantId,
    string CombatantName,
    string AbilityId,
    string AbilityName,
    IReadOnlyList<string> TargetIds,
    string? WastedReason,
    string FeedText);
