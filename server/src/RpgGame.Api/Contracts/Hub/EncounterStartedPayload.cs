namespace RpgGame.Api.Contracts.Hub;

public record EncounterStartedPayload(
    string EncounterId,
    List<CombatantDto> Combatants,
    MapDto Map);
