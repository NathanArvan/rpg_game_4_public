namespace RpgGame.Api.Contracts.Hub;

/// <param name="ApCosts">AP cost per action type, keyed by wire type ("move", "melee_attack", …) (3B.2).</param>
public record PlanningPhaseStartedPayload(
    int RoundNumber,
    int PlanningTimerDurationSeconds,
    IReadOnlyList<PlanningCombatantDto> Combatants,
    Dictionary<string, int> ApCosts);

public record PlanningCombatantDto(
    string Id,
    string UserId,
    string CharacterName,
    int CurrentHp,
    int MaxHp,
    bool IsDefeated,
    string CombatantType = "player",
    bool IsAiControlled = false,
    string ProfileId = "",
    string ProfileName = "",
    int MovementRange = 6,
    int MaxAp = 4);
