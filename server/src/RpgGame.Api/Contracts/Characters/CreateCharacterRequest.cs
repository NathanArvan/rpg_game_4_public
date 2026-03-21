namespace RpgGame.Api.Contracts.Characters;

public record CreateCharacterRequest(string Name, string? Race, string? Class, int Level, BaseStatsDto BaseStats);
