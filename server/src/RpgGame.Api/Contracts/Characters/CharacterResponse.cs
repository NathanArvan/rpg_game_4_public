namespace RpgGame.Api.Contracts.Characters;

public record CharacterResponse(Guid Id, string Name, string Race, string Class, int Level, BaseStatsDto BaseStats, int MaxHp, int CurrentHp, int MovementRange, int VisionRange);
