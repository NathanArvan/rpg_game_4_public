namespace RpgGame.Api.Contracts.Characters;

public record UpdateCharacterRequest(string Name, string? Race, string? Class, int Level, BaseStatsDto BaseStats);
