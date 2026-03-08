namespace RpgGame.Api.Contracts.Campaigns;

public record StartSessionResponse(string JoinCode, Guid GameSessionId);
