namespace RpgGame.Api.Contracts.Hub;

public record PlanningPhaseStartedPayload(string EncounterId, int RoundNumber, long PlanningTimerExpiresAt);

public record ActionPlannedPayload(string CombatantId, string ActionType);

public record PlayerLockedInPayload(string CombatantId, string CharacterName, bool IsGmEnemies = false);

public record AllLockedInPayload(string EncounterId);

public record EnemyActionPlannedPayload(string CombatantId, string ActionType);
