namespace RpgGame.Rules;

// Conditions (roadmap 3B.4, docs/roadmap/SPRINT_3B4.md). A condition is a named, time-limited bundle of
// modifiers. Abilities put them on combatants through ApplyConditionEffect (and Defend grants Guarded).
// Everything that asks "what do this combatant's conditions do?" goes through ConditionMath.

/// <summary>Which attacks an accuracy or dodge modifier applies to.</summary>
public enum AttackKindFilter
{
    Any,
    Melee,
    Ranged,
}

/// <summary>What a condition stops its bearer from doing (3B.4 PR 3).</summary>
public enum BlockKind
{
    /// <summary>No queued action resolves (Stunned).</summary>
    All,
    /// <summary>No moves, plain or from an ability (Rooted).</summary>
    Move,
}

/// <summary>Whether a periodic effect hurts or heals.</summary>
public enum PeriodicKind
{
    Damage,
    Heal,
}

/// <summary>
/// Changes the damage of each hit the bearer takes (SPRINT_3B4 C3, C4): <see cref="Flat"/> is added, then
/// the result is scaled by <c>(100 + Percent) / 100</c>, rounded down, never below 0.
/// </summary>
/// <param name="Percent">−50 = resistant, +50 = vulnerable, −100 = immune.</param>
/// <param name="Flat">−2 = armour-like, +2 = "marked".</param>
/// <param name="Tags">Only damage from a source with one of these tags; empty = all damage.</param>
public sealed record DamageTakenModifier(int Percent = 0, int Flat = 0, IReadOnlyList<string>? Tags = null)
{
    public IReadOnlyList<string> Tags { get; init; } = Tags ?? [];

    /// <summary>Applies to damage from a source with these tags.</summary>
    public bool AppliesTo(IReadOnlyCollection<string> sourceTags) =>
        Tags.Count == 0 || Tags.Any(sourceTags.Contains);

    public bool Equals(DamageTakenModifier? other) =>
        other is not null && Percent == other.Percent && Flat == other.Flat && Tags.SequenceEqual(other.Tags);

    public override int GetHashCode() => HashCode.Combine(Percent, Flat, Tags.Count);
}

/// <summary>Damage or healing every round (3B.4 PR 2): <c>die + Bonus</c>.</summary>
/// <param name="Die">The die to roll, or null for no die (only <paramref name="Bonus"/>).</param>
public sealed record PeriodicEffect(PeriodicKind Kind, DamageDie? Die, int Bonus = 0);

/// <summary>
/// Everything a condition can change (SPRINT_3B4 C3). All optional; a condition usually sets one or two.
/// Numbers from several active conditions are summed (<see cref="ConditionMath"/>).
/// </summary>
/// <param name="Accuracy">Added to the d20 of the bearer's attacks (of <paramref name="AttackKind"/>).</param>
/// <param name="Dodge">Added to the bearer's Dodge (against attacks of <paramref name="AttackKind"/>).</param>
/// <param name="AttackKind">Which attacks <paramref name="Accuracy"/> and <paramref name="Dodge"/> apply to.</param>
/// <param name="DamageDealt">Flat damage added to each of the bearer's hits.</param>
/// <param name="DamageTaken">Changes each hit the bearer takes.</param>
/// <param name="Movement">± cells to the bearer's movement budget, from the next planning phase (3B.4 PR 3).</param>
/// <param name="Ap">± AP to the bearer's AP budget, from the next planning phase (3B.4 PR 3).</param>
/// <param name="Periodic">Damage or healing at the end of every round (3B.4 PR 2).</param>
/// <param name="Blocks">What the bearer can't do (3B.4 PR 3).</param>
public sealed record Modifiers(
    int Accuracy = 0,
    int Dodge = 0,
    AttackKindFilter AttackKind = AttackKindFilter.Any,
    int DamageDealt = 0,
    DamageTakenModifier? DamageTaken = null,
    int Movement = 0,
    int Ap = 0,
    PeriodicEffect? Periodic = null,
    IReadOnlyList<BlockKind>? Blocks = null)
{
    public static Modifiers None { get; } = new();

    public IReadOnlyList<BlockKind> Blocks { get; init; } = Blocks ?? [];

    public bool Equals(Modifiers? other) =>
        other is not null
        && Accuracy == other.Accuracy && Dodge == other.Dodge && AttackKind == other.AttackKind
        && DamageDealt == other.DamageDealt && Equals(DamageTaken, other.DamageTaken)
        && Movement == other.Movement && Ap == other.Ap && Periodic == other.Periodic
        && Blocks.SequenceEqual(other.Blocks);

    public override int GetHashCode() =>
        HashCode.Combine(Accuracy, Dodge, AttackKind, DamageDealt, DamageTaken, Movement, Ap, HashCode.Combine(Periodic, Blocks.Count));
}

/// <summary>
/// A condition (SPRINT_3B4 C1): a name and its modifiers. How long it lasts belongs to whoever applies it
/// (<see cref="ApplyConditionEffect.Duration"/>), so a short and a long version share one definition.
/// </summary>
/// <param name="Harmful">A debuff: resistible (C9), and the enemy AI doesn't stack it.</param>
/// <param name="Tags">Tags of the condition's periodic damage (e.g. <c>flame</c>), for damage-taken filters.</param>
public sealed record ConditionDefinition(
    string Id,
    string Name,
    bool Harmful,
    IReadOnlyList<string> Tags,
    Modifiers Modifiers)
{
    public bool Equals(ConditionDefinition? other) =>
        other is not null
        && Id == other.Id && Name == other.Name && Harmful == other.Harmful
        && Tags.SequenceEqual(other.Tags) && Equals(Modifiers, other.Modifiers);

    public override int GetHashCode() => HashCode.Combine(Id, Harmful, Modifiers);
}

/// <summary>
/// A condition on a combatant. <see cref="RoundsLeft"/> counts the full rounds it lasts <b>after the
/// current one</b> (SPRINT_3B4 C5): 0 = until the end of this round, 1 = until the end of next round.
/// At the end of each round a condition at 0 is removed and any other goes down by one.
/// </summary>
/// <param name="SourceId">Who applied it (for the feed; not used by the rules).</param>
public sealed record ActiveCondition(ConditionDefinition Definition, int RoundsLeft, string? SourceId = null)
{
    public string Id => Definition.Id;
}

/// <summary>
/// The only place that works out what a combatant's conditions do (SPRINT_3B4 C3, C4). Pure. Used by the
/// resolver, <see cref="PlanValidator"/> and <see cref="EnemyAi"/>; the Api uses it for the client payload.
/// </summary>
public static class ConditionMath
{
    /// <summary>Total accuracy bonus for an attack of <paramref name="kind"/> (none for attacks that can't miss).</summary>
    public static int AccuracyBonus(IReadOnlyList<ActiveCondition> conditions, AttackKind kind) =>
        kind == AttackKind.None ? 0 : conditions.Where(c => Matches(c.Definition.Modifiers.AttackKind, kind)).Sum(c => c.Definition.Modifiers.Accuracy);

    /// <summary>Total dodge bonus against an attack of <paramref name="kind"/>.</summary>
    public static int DodgeBonus(IReadOnlyList<ActiveCondition> conditions, AttackKind kind) =>
        kind == AttackKind.None ? 0 : conditions.Where(c => Matches(c.Definition.Modifiers.AttackKind, kind)).Sum(c => c.Definition.Modifiers.Dodge);

    /// <summary>Flat damage added to each of the bearer's hits.</summary>
    public static int DamageDealtBonus(IReadOnlyList<ActiveCondition> conditions) =>
        conditions.Sum(c => c.Definition.Modifiers.DamageDealt);

    /// <summary>
    /// The damage of one hit after the target's damage-taken modifiers (C4): flat amounts are added, then the
    /// summed percentage (clamped to −100..+100) scales it, rounded down, never below 0. Only modifiers whose
    /// tags match <paramref name="sourceTags"/> count.
    /// </summary>
    public static int ApplyDamageTaken(int damage, IReadOnlyList<ActiveCondition> conditions, IReadOnlyCollection<string> sourceTags)
    {
        int flat = 0, percent = 0;
        foreach (var c in conditions)
        {
            if (c.Definition.Modifiers.DamageTaken is not { } taken || !taken.AppliesTo(sourceTags)) continue;
            flat += taken.Flat;
            percent += taken.Percent;
        }
        if (flat == 0 && percent == 0) return damage;
        percent = Math.Clamp(percent, -100, 100);
        long scaled = (long)Math.Max(0, damage + flat) * (100 + percent);
        return (int)Math.Floor(scaled / 100.0);
    }

    /// <summary>
    /// The movement budget for a round (C3, C6): the profile's range plus every condition's movement, never
    /// below 0. Worked out from the conditions at the start of the planning phase and used for the whole round.
    /// </summary>
    public static int EffectiveMovementRange(CombatProfile profile, IReadOnlyList<ActiveCondition> conditions) =>
        Math.Max(0, profile.MovementRange + conditions.Sum(c => c.Definition.Modifiers.Movement));

    /// <summary>The AP budget for a round (C3, C6): the profile's AP plus every condition's AP, never below 0.</summary>
    public static int EffectiveMaxAp(CombatProfile profile, IReadOnlyList<ActiveCondition> conditions) =>
        Math.Max(0, profile.MaxAp + conditions.Sum(c => c.Definition.Modifiers.Ap));

    /// <summary>
    /// The profile with its round budgets (<see cref="CombatProfile.MaxAp"/>, <see cref="CombatProfile.MovementRange"/>)
    /// replaced by the effective ones, so budget checks written against a profile work unchanged.
    /// </summary>
    public static CombatProfile EffectiveProfile(CombatProfile profile, IReadOnlyList<ActiveCondition> conditions) =>
        conditions.Count == 0
            ? profile
            : profile with
            {
                MaxAp = EffectiveMaxAp(profile, conditions),
                MovementRange = EffectiveMovementRange(profile, conditions),
            };

    /// <summary>
    /// Whether the conditions stop the bearer doing <paramref name="kind"/> (C8). <see cref="BlockKind.All"/>
    /// also blocks moves.
    /// </summary>
    public static bool IsBlocked(IReadOnlyList<ActiveCondition> conditions, BlockKind kind) =>
        BlockingCondition(conditions, kind) is not null;

    /// <summary>The first condition that blocks <paramref name="kind"/> (for the feed), or null.</summary>
    public static ActiveCondition? BlockingCondition(IReadOnlyList<ActiveCondition> conditions, BlockKind kind) =>
        conditions.FirstOrDefault(c => c.Definition.Modifiers.Blocks.Contains(BlockKind.All))
        ?? (kind == BlockKind.Move ? conditions.FirstOrDefault(c => c.Definition.Modifiers.Blocks.Contains(BlockKind.Move)) : null);

    /// <summary>
    /// The bearer skips the round at planning (C8): its conditions block every action, or leave it 0 AP.
    /// The Api locks such a combatant in with an empty plan; the enemy AI doesn't plan it.
    /// </summary>
    public static bool CannotAct(CombatProfile profile, IReadOnlyList<ActiveCondition> conditions) =>
        IsBlocked(conditions, BlockKind.All) || EffectiveMaxAp(profile, conditions) == 0;

    private static bool Matches(AttackKindFilter filter, AttackKind kind) => filter switch
    {
        AttackKindFilter.Melee => kind == AttackKind.Melee,
        AttackKindFilter.Ranged => kind == AttackKind.Ranged,
        _ => true,
    };

    /// <summary>
    /// The conditions after <paramref name="condition"/> is applied (C12): the same condition again doesn't
    /// stack, it keeps the longer duration and the newest source; a new one is added at the end.
    /// </summary>
    /// <param name="refreshed">True when the condition was already there.</param>
    public static IReadOnlyList<ActiveCondition> Apply(
        IReadOnlyList<ActiveCondition> conditions, ActiveCondition condition, out bool refreshed)
    {
        var list = conditions.ToList();
        int i = list.FindIndex(c => c.Id == condition.Id);
        refreshed = i >= 0;
        if (refreshed)
            list[i] = condition with { RoundsLeft = Math.Max(list[i].RoundsLeft, condition.RoundsLeft) };
        else
            list.Add(condition);
        return list;
    }

    /// <summary>
    /// End of round (C5): a condition with 0 rounds left is removed, any other goes down by one.
    /// </summary>
    /// <param name="expired">The ids of the conditions removed, in order.</param>
    public static IReadOnlyList<ActiveCondition> CountDown(IReadOnlyList<ActiveCondition> conditions, out List<string> expired)
    {
        expired = conditions.Where(c => c.RoundsLeft <= 0).Select(c => c.Id).ToList();
        return conditions.Where(c => c.RoundsLeft > 0).Select(c => c with { RoundsLeft = c.RoundsLeft - 1 }).ToList();
    }

    /// <summary>The source tags of an attack (C4): the ability's tags, or melee/ranged for a basic attack.</summary>
    public static IReadOnlyCollection<string> AttackTags(AbilityDefinition? ability, AttackKind kind) =>
        ability is not null ? ability.Tags
        : kind == AttackKind.Melee ? MeleeTags
        : kind == AttackKind.Ranged ? RangedTags
        : [];

    private static readonly string[] MeleeTags = ["melee"];
    private static readonly string[] RangedTags = ["ranged"];
}
