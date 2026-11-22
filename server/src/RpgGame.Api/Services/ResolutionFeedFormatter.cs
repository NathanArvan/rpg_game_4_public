using RpgGame.Rules;

namespace RpgGame.Api.Services;

/// <summary>
/// Feed text for resolution results. Presentation, so it lives in the Api layer rather than
/// in RpgGame.Rules. These strings reach players verbatim; the client adds its own
/// "X moves to (x, y)" / "X stays put" line for movements.
/// </summary>
internal static class ResolutionFeedFormatter
{
    /// <param name="abilityName">The ability's name when an ability made the attack (3B.3).</param>
    public static string Attack(AttackOutcome attack, string attackerName, string targetName, string? abilityName = null)
    {
        if (abilityName is not null) return AbilityAttack(attack, attackerName, targetName, abilityName);

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

    /// <summary>An attack made by an ability (3B.3), named after it: "Brakka's Power Strike hits Goblin for 9 damage".</summary>
    private static string AbilityAttack(AttackOutcome attack, string attackerName, string targetName, string abilityName)
    {
        var who = $"{attackerName}'s {abilityName}";
        if (attack.Wasted)
            return $"{who} on {targetName} fails — {WastedReasonText(attack.WastedReason)}";
        if (!attack.Hit)
            return $"{who} misses {targetName}";
        if (attack.FinishingBlow)
            return $"{who} hits {targetName} — finished off!";
        if (attack.TargetStatus == CombatantStatus.Downed)
            return $"{who} hits {targetName} for {attack.Damage} damage — goes down!";
        if (attack.TargetDefeated)
            return $"{who} hits {targetName} for {attack.Damage} damage — defeated!";
        return $"{who} hits {targetName} for {attack.Damage} damage (HP: {attack.TargetHpBefore} → {attack.TargetHpAfter})";
    }

    /// <summary>
    /// One ability activation (3B.3): "Brakka uses Power Strike on Goblin", or why it was refused.
    /// The attacks it made have their own lines.
    /// </summary>
    public static string AbilityUse(AbilityUse use, string combatantName, string abilityName, IReadOnlyList<string> targetNames) =>
        use.Wasted
            ? $"{combatantName} can't use {abilityName} — {WastedReasonText(use.WastedReason)}"
            : targetNames.Count == 0
                ? $"{combatantName} uses {abilityName}"
                : $"{combatantName} uses {abilityName} on {string.Join(", ", targetNames)}";

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
        WastedReason.OutOfRange => "target out of range",
        WastedReason.OnCooldown => "still on cooldown",
        WastedReason.NotInLoadout => "doesn't have that ability",
        WastedReason.InvalidTargetType => "can't target that combatant",
        WastedReason.NoTargets => "no targets in range",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
