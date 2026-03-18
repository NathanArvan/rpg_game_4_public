namespace RpgGame.Api.Contracts.Hub;

public abstract record EndTurnResult(Guid SessionId);

public sealed record TurnAdvancedResult(Guid SessionId, TurnAdvancedPayload Payload) : EndTurnResult(SessionId);

public sealed record EncounterEndedResult(Guid SessionId, EncounterEndedPayload Payload) : EndTurnResult(SessionId);
