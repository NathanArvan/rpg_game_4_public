namespace RpgGame.Api.Contracts.Hub;

public static class HubEvents
{
    public static class Events
    {
        public const string PlayerPresenceChanged = nameof(PlayerPresenceChanged);
        public const string EncounterStarted      = nameof(EncounterStarted);
        public const string TokenMoved            = nameof(TokenMoved);
        public const string ActionRejected        = nameof(ActionRejected);
        public const string AttackResolved        = nameof(AttackResolved);
        public const string CombatantDefeated     = nameof(CombatantDefeated);
        public const string EncounterEnded        = nameof(EncounterEnded);
        public const string PlanningPhaseStarted  = nameof(PlanningPhaseStarted);
        public const string ActionPlanned         = nameof(ActionPlanned);
        public const string PlayerLockedIn        = nameof(PlayerLockedIn);
        public const string AllLockedIn           = nameof(AllLockedIn);
        public const string ResolutionCompleted  = nameof(ResolutionCompleted);
    }

    public static class Methods
    {
        public const string JoinSession     = nameof(JoinSession);
        public const string StartEncounter  = nameof(StartEncounter);
        public const string PlanAction      = nameof(PlanAction);
        public const string LockIn          = nameof(LockIn);
        public const string ForceResolve    = nameof(ForceResolve);
    }
}
