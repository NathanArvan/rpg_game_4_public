namespace RpgGame.Api.Contracts.Hub;

public record EncounterStartedPayload(
    string EncounterId,
    string ActiveTurnCombatantId,
    List<CombatantDto> Combatants,
    MapDto Map);
