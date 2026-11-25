namespace RpgGame.Api.Contracts.Hub;

/// <param name="Type">"move", "disengage" (3B.6), "melee_attack", "ranged_attack", "defend", "stabilize" or "ability" (3B.3).</param>
/// <param name="AbilityId">For "ability": which of the combatant's abilities.</param>
/// <param name="TargetCombatantIds">For abilities that pick several targets (3B.3, later PR).</param>
public record PlannedActionEntry(
    string Type,
    PositionDto? TargetCell,
    string? TargetCombatantId,
    string? AbilityId = null,
    IReadOnlyList<string>? TargetCombatantIds = null);

public record PlannedActionArray(IReadOnlyList<PlannedActionEntry> Actions);
