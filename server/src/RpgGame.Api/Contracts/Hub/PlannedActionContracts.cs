namespace RpgGame.Api.Contracts.Hub;

public record PlannedActionEntry(
    string Type,
    PositionDto? TargetCell,
    string? TargetCombatantId);

public record PlannedActionArray(IReadOnlyList<PlannedActionEntry> Actions);
