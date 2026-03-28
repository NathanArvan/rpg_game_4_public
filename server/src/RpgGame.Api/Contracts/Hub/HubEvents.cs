namespace RpgGame.Api.Contracts.Hub;

public static class HubEvents
{
    public static class Events
    {
        public const string PlayerPresenceChanged = nameof(PlayerPresenceChanged);
        public const string EncounterStarted      = nameof(EncounterStarted);
        public const string TokenMoved            = nameof(TokenMoved);
        public const string TurnAdvanced          = nameof(TurnAdvanced);
        public const string ActionRejected        = nameof(ActionRejected);
        public const string AttackResolved        = nameof(AttackResolved);
        public const string CombatantDefeated     = nameof(CombatantDefeated);
        public const string EncounterEnded        = nameof(EncounterEnded);
    }

    public static class Methods
    {
        public const string JoinSession     = nameof(JoinSession);
        public const string StartEncounter  = nameof(StartEncounter);
        public const string MoveToken       = nameof(MoveToken);
        public const string DeclareAttack   = nameof(DeclareAttack);
        public const string EndTurn         = nameof(EndTurn);
    }
}
