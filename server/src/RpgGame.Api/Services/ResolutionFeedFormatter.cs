using RpgGame.Rules;

namespace RpgGame.Api.Services;

/// <summary>
/// Feed text for resolution results. Presentation, so it lives in the Api layer rather than
/// in RpgGame.Rules. These strings reach players verbatim; the client adds its own
/// "X moves to (x, y)" / "X stays put" line for movements.
/// </summary>
internal static class ResolutionFeedFormatter
{
    public static string Attack(AttackOutcome attack, string attackerName, string targetName)
    {
        bool ranged = attack.AttackType == ActionType.RangedAttack;
        string attackVerb = ranged ? "shoots at" : "strikes";
        string missVerb = ranged ? "shoots at" : "swings at";

        if (attack.Wasted)
            return $"{attackerName}'s attack on {targetName} fails — {WastedReasonText(attack.WastedReason)}";
        if (!attack.Hit)
            return $"{attackerName} {missVerb} {targetName} and misses";
        if (attack.FinishingBlow)
            return $"{attackerName} {attackVerb} {targetName} — finished off!";
        if (attack.TargetStatus == CombatantStatus.Downed)
            return $"{attackerName} {attackVerb} {targetName} for {attack.Damage} damage — goes down!";
        if (attack.TargetDefeated)
            return $"{attackerName} {attackVerb} {targetName} for {attack.Damage} damage — defeated!";
        return $"{attackerName} {attackVerb} {targetName} for {attack.Damage} damage (HP: {attack.TargetHpBefore} → {attack.TargetHpAfter})";
    }

    /// <param name="targetName">Stabilize: the target's name.</param>
    public static string Effect(EffectOutcome effect, string combatantName, string targetName = "") => effect.Type switch
    {
        EffectType.Defend => $"{combatantName} holds position",
        EffectType.Stabilize when effect.Wasted => $"{combatantName}'s stabilize on {targetName} fails — {WastedReasonText(effect.WastedReason)}",
        EffectType.Stabilize => $"{combatantName} stabilizes {targetName}",
        _ => throw new ArgumentOutOfRangeException(nameof(effect), effect.Type, null),
    };

    /// <summary>End-of-round bleedout line (3B.5).</summary>
    public static string Bleedout(BleedoutOutcome bleedout, string name) => bleedout.Died
        ? $"{name} bleeds out"
        : $"{name} is bleeding out — {bleedout.RoundsLeft} {(bleedout.RoundsLeft == 1 ? "round" : "rounds")} left";

    private static string WastedReasonText(WastedReason reason) => reason switch
    {
        WastedReason.TargetDefeated => "target already defeated",
        WastedReason.OutOfMeleeReach => "target out of melee range",
        WastedReason.InvalidTarget => "invalid target",
        WastedReason.TargetNotDowned => "target isn't bleeding out",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
