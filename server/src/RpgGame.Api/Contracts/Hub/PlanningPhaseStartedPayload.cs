namespace RpgGame.Api.Contracts.Hub;

public record PlanningPhaseStartedPayload(
    int RoundNumber,
    int PlanningTimerDurationSeconds,
    IReadOnlyList<PlanningCombatantDto> Combatants);

public record PlanningCombatantDto(
    string Id,
    string UserId,
    string CharacterName,
    int CurrentHp,
    int MaxHp,
    bool IsDefeated,
    string CombatantType = "player",
    bool IsAiControlled = false);
