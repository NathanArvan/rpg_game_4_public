namespace RpgGame.Api.Contracts.Hub;

public record JoinSessionResult(
    Guid SessionId,
    IReadOnlyList<PlayerPresenceChangedPayload> ExistingPlayerPayloads,
    PlayerPresenceChangedPayload NewPlayerPayload);

public record EncounterStartedResult(Guid SessionId, EncounterStartedPayload Payload);

public record TokenMovedResult(Guid SessionId, TokenMovedPayload Payload);

public record AttackResult(
    Guid SessionId,
    AttackResolvedPayload AttackPayload,
    CombatantDefeatedPayload? DefeatedPayload);
