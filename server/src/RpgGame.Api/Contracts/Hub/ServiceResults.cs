namespace RpgGame.Api.Contracts.Hub;

public record JoinSessionResult(
    Guid SessionId,
    IReadOnlyList<PlayerPresenceChangedPayload> ExistingPlayerPayloads,
    PlayerPresenceChangedPayload NewPlayerPayload);

public record EncounterStartedResult(Guid SessionId, EncounterStartedPayload Payload);

public record AttackResult(
    Guid SessionId,
    AttackResolvedPayload AttackPayload,
    CombatantDefeatedPayload? DefeatedPayload);

public record PlanActionResult(Guid SessionId, string CombatantId, string ActionType);

public record LockInResult(Guid SessionId, string CombatantId, string CharacterName, bool AllLockedIn, string EncounterId);

public record ForceResolveResult(Guid SessionId, string EncounterId);

public record ResolutionResult(Guid SessionId, ResolutionCompletedPayload Payload, PlanningPhaseStartedPayload? NextRoundPayload);
