namespace RpgGame.Api.Contracts.Hub;

public record MonsterAddedPayload(PlanningCombatantDto Monster, PositionDto SpawnPosition);
