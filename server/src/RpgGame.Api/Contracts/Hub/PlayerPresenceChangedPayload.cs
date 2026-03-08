namespace RpgGame.Api.Contracts.Hub;

public record PlayerPresenceChangedPayload(string UserId, string DisplayName, string? CharacterId, bool IsOnline);
