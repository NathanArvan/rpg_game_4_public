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
        var damage = $"{attack.Damage} damage{ConditionSuffix(attack)}";
        if (attack.TargetStatus == CombatantStatus.Downed)
            return $"{attackerName} {attackVerb} {targetName} for {damage} — goes down!";
        if (attack.TargetDefeated)
            return $"{attackerName} {attackVerb} {targetName} for {damage} — defeated!";
        return $"{attackerName} {attackVerb} {targetName} for {damage} (HP: {attack.TargetHpBefore} → {attack.TargetHpAfter})";
    }

    /// <summary>
    /// An attack made by an ability (3B.3), named after it: "Brakka's Power Strike hits Goblin for 9 damage".
    /// A bonus for moving first shows after the damage: "for 8 damage (+3 from moving)".
    /// </summary>
    private static string AbilityAttack(AttackOutcome attack, string attackerName, string targetName, string abilityName)
    {
        var who = $"{attackerName}'s {abilityName}";
        var damage = (attack.MoveBonus > 0 ? $"{attack.Damage} damage (+{attack.MoveBonus} from moving)" : $"{attack.Damage} damage")
            + ConditionSuffix(attack);
        if (attack.Wasted)
            return $"{who} on {targetName} fails — {WastedReasonText(attack.WastedReason)}";
        if (!attack.Hit)
            return $"{who} misses {targetName}";
        if (attack.FinishingBlow)
            return $"{who} hits {targetName} — finished off!";
        if (attack.TargetStatus == CombatantStatus.Downed)
            return $"{who} hits {targetName} for {damage} — goes down!";
        if (attack.TargetDefeated)
            return $"{who} hits {targetName} for {damage} — defeated!";
        return $"{who} hits {targetName} for {damage} (HP: {attack.TargetHpBefore} → {attack.TargetHpAfter})";
    }

    /// <summary>
    /// How much conditions changed a hit (3B.4): " (+2 from conditions)" / " (-4 from conditions)", or nothing.
    /// Empty when they changed nothing, so every line from before 3B.4 reads exactly as it did.
    /// </summary>
    private static string ConditionSuffix(AttackOutcome attack) =>
        attack.ConditionDamage == 0 || !attack.Hit || attack.FinishingBlow ? ""
        : attack.ConditionDamage > 0 ? $" (+{attack.ConditionDamage} from conditions)"
        : $" ({attack.ConditionDamage} from conditions)";

    /// <summary>
    /// One condition on one target (3B.4): "Kestrel's Mark: Goblin is Marked until the end of next round",
    /// "Brakka is Guarded until the end of the round" (Defend), a refresh, a resist or why it failed.
    /// </summary>
    /// <param name="abilityName">The ability's name, or null for Defend's Guarded.</param>
    public static string Condition(
        ConditionOutcome condition, string sourceName, string targetName, string conditionName, string? abilityName)
    {
        var who = abilityName is null ? "" : $"{sourceName}'s {abilityName}: ";
        return condition.Result switch
        {
            ConditionResultKind.Applied => $"{who}{targetName} is {conditionName} {Duration(condition.RoundsLeft)}",
            ConditionResultKind.Refreshed => $"{who}{targetName} is still {conditionName}, now {Duration(condition.RoundsLeft)}",
            ConditionResultKind.Resisted => $"{targetName} resists {conditionName}",
            _ when condition.WastedReason == WastedReason.Missed =>
                $"{who}{targetName} isn't {conditionName} — the attack missed",
            _ => $"{(abilityName is null ? sourceName : $"{sourceName}'s {abilityName}")} on {targetName} fails — {WastedReasonText(condition.WastedReason)}",
        };
    }

    /// <summary>End-of-round line for a condition that ran out (3B.4).</summary>
    public static string ConditionExpired(string combatantName, string conditionName) =>
        $"{combatantName} is no longer {conditionName}";

    /// <summary>
    /// How long a condition lasts, in words (SPRINT_3B4 C5): players never see the number. 0 = until the
    /// end of the round, 1 = until the end of next round, N = for N more rounds.
    /// </summary>
    public static string Duration(int roundsLeft) => roundsLeft switch
    {
        <= 0 => "until the end of the round",
        1 => "until the end of next round",
        _ => $"for {roundsLeft} more rounds",
    };

    /// <summary>
    /// One ability activation (3B.3): "Brakka uses Power Strike on Goblin", or why it was refused.
    /// The attacks it made have their own lines.
    /// </summary>
    public static string AbilityUse(AbilityUse use, string combatantName, string abilityName, IReadOnlyList<string> targetNames) =>
        use.WastedReason == WastedReason.NoTargets
            ? $"{combatantName} uses {abilityName} — {WastedReasonText(use.WastedReason)}"
            : use.Wasted
            ? $"{combatantName} can't use {abilityName} — {WastedReasonText(use.WastedReason)}"
            : targetNames.Count == 0
                ? $"{combatantName} uses {abilityName}"
                : $"{combatantName} uses {abilityName} on {string.Join(", ", targetNames)}";

    /// <summary>
    /// One heal (3B.3): "Mira's Mend heals Brakka for 6 (HP: 0 → 6) — back on their feet!". HP is
    /// shown after netting with the tick's damage and the max-HP cap.
    /// </summary>
    public static string Heal(HealOutcome heal, string healerName, string targetName, string abilityName)
    {
        var who = $"{healerName}'s {abilityName}";
        if (heal.Wasted)
            return $"{who} on {targetName} fails — {WastedReasonText(heal.WastedReason)}";
        if (heal.TargetStatus == CombatantStatus.Dead)
            return $"{who} on {targetName} comes too late";
        var hp = $"(HP: {heal.TargetHpBefore} → {heal.TargetHpAfter})";
        if (heal.Revived)
            return $"{who} heals {targetName} for {heal.Amount} {hp} — back on their feet!";
        if (heal.TargetStatus != CombatantStatus.Standing)
            return $"{who} heals {targetName} for {heal.Amount} {hp}, but they stay down";
        return $"{who} heals {targetName} for {heal.Amount} {hp}";
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
        WastedReason.OutOfRange => "target out of range",
        WastedReason.OnCooldown => "still on cooldown",
        WastedReason.NotInLoadout => "doesn't have that ability",
        WastedReason.InvalidTargetType => "can't target that combatant",
        WastedReason.NoTargets => "no targets in range",
        WastedReason.Missed => "the attack missed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
