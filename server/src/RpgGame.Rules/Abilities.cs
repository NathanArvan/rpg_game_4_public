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
    /// range of the caster, nearest first (ties in combatant order), at most <see cref="Targeting.Count"/>.
    /// </summary>
    Auto,
    /// <summary>A grid cell (4.1, reserved).</summary>
    Cell,
    /// <summary>
    /// Prepared reactions only (3B.6, SPRINT_3B6 X6): whoever set off the reaction, e.g. the enemy that stepped into
    /// reach. Nothing is chosen when it's planned.
    /// </summary>
    Trigger,
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

/// <summary>How an ability is used (ABILITIES.md A1).</summary>
public enum AbilityActivation
{
    /// <summary>Queued and resolved at its queue position.</summary>
    Active,
    /// <summary>
    /// Queued as the last action of a queue; from its tick to the end of the round it watches for its
    /// <see cref="AbilityDefinition.Trigger"/> and resolves the moment that happens (3B.6, SPRINT_3B6 X6–X9).
    /// </summary>
    Prepared,
    /// <summary>Always on, never queued (reserved).</summary>
    Passive,
}

/// <summary>What sets off a prepared reaction (3B.6, SPRINT_3B6 X6, X12, X13).</summary>
public enum TriggerKind
{
    /// <summary>An enemy's movement step ends within the watcher's melee reach, having started outside it (Brace).</summary>
    EnemyEntersReach,
    /// <summary>An enemy takes a movement step that ends within <see cref="ReactionTrigger.Range"/> of the watcher (Overwatch).</summary>
    EnemyMovesWithin,
    /// <summary>The watcher is the target of an attack this tick, checked before the Attack phase rolls (Parry, 3B.6 PR 3).</summary>
    Attacked,
    /// <summary>
    /// An ally within <see cref="ReactionTrigger.Range"/> (default melee reach) is the target of a single-target attack by an
    /// enemy, checked before the Attack phase rolls (Intercept, 3B.6 PR 3).
    /// </summary>
    AllyAttacked,
}

/// <summary>The trigger of a prepared ability (3B.6, SPRINT_3B6 X6).</summary>
/// <param name="Range">Cells from the watcher, for <see cref="TriggerKind.EnemyMovesWithin"/> and <see cref="TriggerKind.AllyAttacked"/>.</param>
/// <param name="AttackKind">For <see cref="TriggerKind.Attacked"/>: only attacks of this kind, or null for any.</param>
public sealed record ReactionTrigger(TriggerKind Kind, int? Range = null, AttackKind? AttackKind = null);

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
/// <param name="BonusIfMoved">Extra flat damage when the same activation moved the caster far enough (Charge).</param>
public sealed record DamageSpec(DamageDie? Die = null, int Bonus = 0, int Multiplier = 1, MovedBonus? BonusIfMoved = null)
{
    /// <summary>A basic attack: the caster's damage die, nothing added.</summary>
    public static DamageSpec Weapon { get; } = new();
}

/// <summary>
/// A conditional damage bonus (ABILITIES.md A6): <see cref="Bonus"/> is added when a move effect of the
/// same activation took the caster at least <see cref="MinCells"/> cells this tick.
/// </summary>
public sealed record MovedBonus(int MinCells, int Bonus);

/// <summary>Where a move effect goes.</summary>
public enum MoveTo
{
    /// <summary>A planned cell (<see cref="PlannedAction.TargetCell"/>).</summary>
    Cell,
    /// <summary>
    /// The free cell in melee reach of the ability's target (its position at the start of the tick's
    /// Movement phase) that is closest to the caster.
    /// </summary>
    Target,
}

/// <summary>How a move effect travels.</summary>
public enum MovePath
{
    /// <summary>Cell by cell, in lockstep with everyone else's moves (blocking, Speed tiebreaks).</summary>
    Walk,
    /// <summary>No path, ignores blockers. Reserved; not implemented yet.</summary>
    Teleport,
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

/// <summary>
/// Moves the caster, resolved in the Movement phase together with everyone else's moves (ABILITIES.md
/// A5, A7). Later effects of the same activation see how many cells it covered
/// (<see cref="DamageSpec.BonusIfMoved"/>).
/// </summary>
/// <param name="MaxCells">At most this many cells.</param>
/// <param name="UsesMovementBudget">
/// The cells come out of the round's shared <see cref="CombatProfile.MovementRange"/> (Charge). False:
/// the ability's own cells, on top of the budget (Dash).
/// </param>
/// <param name="Disengage">The move doesn't provoke passive opportunity attacks (3B.6, SPRINT_3B6 X2). No shipped
/// ability sets it yet (a future "Tumble").</param>
public sealed record MoveEffect(
    MoveTo To,
    int MaxCells,
    bool UsesMovementBudget = true,
    MovePath Path = MovePath.Walk,
    bool Disengage = false) : AbilityEffect;

/// <summary>
/// Puts a condition on each target, resolved in the Effects phase in plan order, then target order
/// (SPRINT_3B4 C2). The condition's definition is copied in when the ability is loaded, so the resolver
/// never needs a catalog.
/// </summary>
/// <param name="Duration">Full rounds it lasts after the current one (C5): 0 = until the end of this round.</param>
/// <param name="RequiresHit">Only targets that the same activation's attack hit get it.</param>
public sealed record ApplyConditionEffect(ConditionDefinition Condition, int Duration, bool RequiresHit) : AbilityEffect;

/// <summary>
/// A prepared reaction's defence against the attacks that set it off (3B.6 PR 3, SPRINT_3B6 X13, Parry): once its
/// <see cref="TriggerKind.Attacked"/> trigger fires, the watcher's Dodge is raised by <paramref name="Dodge"/> against every
/// matching attack on it in that tick's Attack phase (they're simultaneous, so it covers the whole batch).
/// </summary>
public sealed record ModifyIncomingEffect(int Dodge) : AbilityEffect;

/// <summary>
/// A prepared reaction that takes an attack meant for an ally (3B.6 PR 3, SPRINT_3B6 X13, Intercept): once its
/// <see cref="TriggerKind.AllyAttacked"/> trigger fires, the first single-target attack on an ally in range is made
/// against the watcher instead. The attacker's reach and range checks are waived; the watcher's own Dodge and
/// conditions apply.
/// </summary>
public sealed record RedirectEffect : AbilityEffect;

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
    /// <summary>What sets off a <see cref="AbilityActivation.Prepared"/> ability (3B.6); null for an active one.</summary>
    public ReactionTrigger? Trigger { get; init; }

    /// <summary>A prepared reaction (3B.6): it ends its queue and watches for <see cref="Trigger"/>.</summary>
    public bool IsPrepared => Activation == AbilityActivation.Prepared;

    // Records compare collections by reference; tags and effects are compared by content.
    public bool Equals(AbilityDefinition? other) =>
        other is not null
        && Id == other.Id && Name == other.Name && ApCost == other.ApCost && Cooldown == other.Cooldown
        && Targeting == other.Targeting && Charges == other.Charges && Activation == other.Activation
        && Trigger == other.Trigger
        && Tags.SequenceEqual(other.Tags) && Effects.SequenceEqual(other.Effects);

    public override int GetHashCode() => HashCode.Combine(Id, ApCost, Cooldown, Targeting, Effects.Count);

    /// <summary>The ability's move effect, or null if it doesn't move the caster.</summary>
    public MoveEffect? Move => Effects.OfType<MoveEffect>().FirstOrDefault();

    /// <summary>The conditions this ability applies (3B.4).</summary>
    public IEnumerable<ApplyConditionEffect> ConditionEffects => Effects.OfType<ApplyConditionEffect>();

    /// <summary>Whether a planned use needs a <see cref="PlannedAction.TargetCell"/> (a move to a cell).</summary>
    public bool NeedsCell => Move is { To: MoveTo.Cell };
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
