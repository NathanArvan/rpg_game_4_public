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
        if (attack.TargetDefeated)
            return $"{attackerName} {attackVerb} {targetName} for {attack.Damage} damage — defeated!";
        return $"{attackerName} {attackVerb} {targetName} for {attack.Damage} damage (HP: {attack.TargetHpBefore} → {attack.TargetHpAfter})";
    }

    public static string Effect(EffectOutcome effect, string combatantName) => effect.Type switch
    {
        EffectType.Defend => $"{combatantName} holds position",
        _ => throw new ArgumentOutOfRangeException(nameof(effect), effect.Type, null),
    };

    private static string WastedReasonText(WastedReason reason) => reason switch
    {
        WastedReason.TargetDefeated => "target already defeated",
        WastedReason.OutOfMeleeReach => "target out of melee range",
        WastedReason.InvalidTarget => "invalid target",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
