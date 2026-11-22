namespace RpgGame.Rules;

// Abilities (roadmap 3B.3, docs/design/ABILITIES.md). An ability is data: what it costs, who it can
// target, and an ordered list of effects. The resolver runs effects; it knows nothing about
// specific abilities. A new kind of effect is one new AbilityEffect subtype plus its handler in
// RoundResolver.

/// <summary>How an ability picks its targets (ABILITIES.md A4).</summary>
public enum TargetKind
{
    /// <summary>The caster only.</summary>
    Self,
    /// <summary>One chosen combatant, tracked like an attack target.</summary>
    Combatant,
    /// <summary>Up to <see cref="Targeting.Count"/> distinct chosen combatants, each checked on its own.</summary>
    Combatants,
    /// <summary>
    /// Found when the ability resolves, after the tick's movement: everyone matching the filter within
    /// range of the caster, nearest first (ties by id), at most <see cref="Targeting.Count"/>.
    /// </summary>
    Auto,
    /// <summary>A grid cell (4.1, reserved).</summary>
    Cell,
}

/// <summary>Who is a legal target, by <see cref="Relation"/> and status.</summary>
public enum TargetFilter
{
    Enemy,
    Ally,
    Any,
    /// <summary>An ally (or the caster, with <see cref="Targeting.IncludeSelf"/>) who is Downed.</summary>
    DownedAlly,
}

/// <summary>How an attack effect checks for a hit.</summary>
public enum AttackKind
{
    /// <summary>d20 + the caster's MeleeAccuracy.</summary>
    Melee,
    /// <summary>d20 + the caster's RangedAccuracy.</summary>
    Ranged,
    /// <summary>Always hits; no d20 is rolled.</summary>
    None,
}

/// <summary>How an ability is used. Only <see cref="Active"/> exists in 3B.3; the others are reserved (3B.6).</summary>
public enum AbilityActivation
{
    /// <summary>Queued and resolved at its queue position.</summary>
    Active,
    /// <summary>Queued; fires when a trigger is met (3B.6).</summary>
    Prepared,
    /// <summary>Always on, never queued (3B.6).</summary>
    Passive,
}

/// <param name="Range">Chebyshev cells from the caster at resolution time (1 = adjacent).</param>
/// <param name="Count"><see cref="TargetKind.Combatants"/>: max chosen targets. <see cref="TargetKind.Auto"/>: max hit (null = all).</param>
/// <param name="IncludeSelf">Ally filters: the caster counts as a legal target.</param>
public sealed record Targeting(
    TargetKind Kind,
    TargetFilter Filter,
    int Range,
    int? Count = null,
    bool IncludeSelf = false);

/// <summary>
/// Damage of an attack effect (ABILITIES.md A6): <c>(die + Bonus) × Multiplier</c>, then doubled on a
/// crit (crits are still disabled).
/// </summary>
/// <param name="Die">The die to roll, or null for the caster's own <see cref="CombatProfile.BaseDamage"/> ("weapon").</param>
/// <param name="Bonus">Flat damage added after the die (the pipeline's abilityModifier slot).</param>
/// <param name="Multiplier">Applied after the bonus, before a crit.</param>
public sealed record DamageSpec(DamageDie? Die = null, int Bonus = 0, int Multiplier = 1)
{
    /// <summary>A basic attack: the caster's damage die, nothing added.</summary>
    public static DamageSpec Weapon { get; } = new();
}

/// <summary>One step of an ability. Each kind resolves in a fixed phase of the tick (ABILITIES.md A5).</summary>
public abstract record AbilityEffect;

/// <summary>An attack on each target, resolved in the Attacks phase with one hit roll per target.</summary>
/// <param name="AccuracyBonus">Added to the caster's accuracy for this attack.</param>
public sealed record AttackEffect(AttackKind Roll, DamageSpec Damage, int AccuracyBonus = 0) : AbilityEffect;

/// <summary>
/// Heals each target, resolved in the Attacks phase: the amount is a positive HP delta netted with the
/// tick's damage, then HP is capped at MaxHp (ABILITIES.md A8). Healing never rolls to hit.
/// </summary>
/// <param name="Die">The die to roll, or null for no die (only <paramref name="Bonus"/>).</param>
/// <param name="Bonus">Flat healing added after the die.</param>
/// <param name="CanRevive">
/// A heal on a Downed or Stable target brings it back to Standing with the healed HP, unless a
/// finishing blow hits it in the same tick. Without this, such targets can't be healed.
/// </param>
public sealed record HealEffect(DamageDie? Die, int Bonus = 0, bool CanRevive = true) : AbilityEffect;

/// <param name="Cooldown">Rounds the ability can't be planned after the round it was used (0 = none).</param>
/// <param name="Tags">Mechanic tags (melee, ranged, spell, …) and resonance tags (flame, holy, …).</param>
/// <param name="Charges">Uses per encounter. Reserved; not implemented yet.</param>
public sealed record AbilityDefinition(
    string Id,
    string Name,
    int ApCost,
    int Cooldown,
    IReadOnlyList<string> Tags,
    Targeting Targeting,
    IReadOnlyList<AbilityEffect> Effects,
    int? Charges = null,
    AbilityActivation Activation = AbilityActivation.Active)
{
    // Records compare collections by reference; tags and effects are compared by content.
    public bool Equals(AbilityDefinition? other) =>
        other is not null
        && Id == other.Id && Name == other.Name && ApCost == other.ApCost && Cooldown == other.Cooldown
        && Targeting == other.Targeting && Charges == other.Charges && Activation == other.Activation
        && Tags.SequenceEqual(other.Tags) && Effects.SequenceEqual(other.Effects);

    public override int GetHashCode() => HashCode.Combine(Id, ApCost, Cooldown, Targeting, Effects.Count);
}

/// <summary>How one combatant stands toward another (ABILITIES.md A3).</summary>
public enum Relation
{
    Self,
    Ally,
    Enemy,
}

/// <summary>
/// The only place that decides who is friendly or hostile (ABILITIES.md A3). Targeting filters,
/// planning checks and the AI all ask here; nothing else compares teams. Non-hostile rival teams
/// (a Neutral relation) would be added here later.
/// </summary>
public static class Relations
{
    /// <summary>Same non-empty team: allies. Anything else, including no team: enemies.</summary>
    public static Relation Between(CombatantState a, CombatantState b)
    {
        if (a.Id == b.Id) return Relation.Self;
        return a.Team.Length > 0 && a.Team == b.Team ? Relation.Ally : Relation.Enemy;
    }

    /// <summary>
    /// Whether <paramref name="target"/> passes <paramref name="targeting"/>'s filter by relation
    /// alone. Status (Downed for <see cref="TargetFilter.DownedAlly"/>) is checked separately.
    /// </summary>
    public static bool Matches(CombatantState caster, CombatantState target, Targeting targeting)
    {
        var relation = Between(caster, target);
        return targeting.Filter switch
        {
            TargetFilter.Enemy => relation == Relation.Enemy,
            TargetFilter.Ally or TargetFilter.DownedAlly =>
                relation == Relation.Ally || (relation == Relation.Self && targeting.IncludeSelf),
            TargetFilter.Any => relation != Relation.Self || targeting.IncludeSelf,
            _ => false,
        };
    }
}
