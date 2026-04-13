namespace RpgGame.Api.Contracts.Hub;

public record ResolutionPayload(
    IReadOnlyList<TickResult> Ticks,
    int NewRoundNumber,
    IReadOnlyList<string> DefeatedCombatantIds,
    IReadOnlyList<PlanningCombatantDto> UpdatedCombatants);

public record TickResult(
    int TickIndex,
    IReadOnlyList<MovementResult> Movements,
    IReadOnlyList<AttackResult> Attacks,
    IReadOnlyList<EffectResult> Effects);

public record MovementResult(
    string CombatantId,
    string CombatantName,
    IReadOnlyList<PositionDto> Path,
    PositionDto FinalPosition,
    bool WasBlocked,
    string? BlockedByCombatantId);

public record AttackResult(
    string AttackerId,
    string AttackerName,
    string TargetId,
    string TargetName,
    string AttackType,
    bool Hit,
    int Damage,
    int TargetNewHp,
    bool TargetDefeated,
    bool Wasted,
    string FeedText);

public record EffectResult(
    string CombatantId,
    string EffectType,
    string Description);
