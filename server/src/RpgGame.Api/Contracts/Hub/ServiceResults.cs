namespace RpgGame.Api.Contracts.Hub;

public record JoinSessionResult(
    Guid SessionId,
    IReadOnlyList<PlayerPresenceChangedPayload> ExistingPlayerPayloads,
    PlayerPresenceChangedPayload NewPlayerPayload);

public record EncounterStartedResult(Guid SessionId, EncounterStartedPayload Payload, PlanningPhaseStartedPayload PlanningPayload);

public record LockInResult(Guid SessionId, string CombatantId, string CharacterName, bool AllLockedIn, string EncounterId);

public record PlanActionResult(bool Success);

public record ForceResolveResult(Guid SessionId, bool AllLockedIn, string EncounterId);

public record AddMonsterResult(
    Guid SessionId,
    string EncounterId,
    PlanningCombatantDto MonsterDto,
    PositionDto SpawnPosition);
