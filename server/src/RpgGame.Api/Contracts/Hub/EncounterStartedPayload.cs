namespace RpgGame.Api.Contracts.Hub;

public record EncounterStartedPayload(
    string EncounterId,
    int RoundNumber,
    string RoundPhase,
    long PlanningTimerExpiresAt,
    List<CombatantDto> Combatants,
    MapDto Map);
