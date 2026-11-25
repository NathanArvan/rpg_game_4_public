namespace RpgGame.Api.Contracts.Hub;

/// <summary>A condition definition (3B.4): what it's called and what it changes.</summary>
/// <param name="Harmful">A debuff (shown in red).</param>
/// <param name="Tags">Tags of its periodic damage (e.g. "flame").</param>
public record ConditionDto(
    string Id,
    string Name,
    string Description,
    bool Harmful,
    IReadOnlyList<string> Tags,
    ConditionModifiersDto Modifiers);

/// <summary>What a condition changes (SPRINT_3B4 C3). Zero / null / empty means "no change".</summary>
/// <param name="AttackKind">"any", "melee" or "ranged": which attacks <paramref name="Accuracy"/> and <paramref name="Dodge"/> apply to.</param>
/// <param name="Movement">± cells of movement per round, from the next round.</param>
/// <param name="Ap">± AP per round, from the next round.</param>
/// <param name="Blocks">"all" (no actions) and/or "move" (no movement).</param>
public record ConditionModifiersDto(
    int Accuracy,
    int Dodge,
    string AttackKind,
    int DamageDealt,
    ConditionDamageTakenDto? DamageTaken,
    int Movement,
    int Ap,
    ConditionPeriodicDto? Periodic,
    IReadOnlyList<string> Blocks);

/// <param name="Percent">−50 = takes half damage, +50 = takes 50% more, −100 = immune.</param>
/// <param name="Tags">Only damage from sources with one of these tags; empty = all damage.</param>
public record ConditionDamageTakenDto(int Percent, int Flat, IReadOnlyList<string> Tags);

/// <param name="Kind">"damage" or "heal", at the end of every round.</param>
/// <param name="Die">A die like "d4", or null for none.</param>
public record ConditionPeriodicDto(string Kind, string? Die, int Bonus);

/// <summary>A condition on a combatant (3B.4).</summary>
/// <param name="RoundsLeft">Full rounds it lasts after the current one: 0 = until the end of this round,
/// 1 = until the end of next round (SPRINT_3B4 C5).</param>
/// <param name="SourceId">Who applied it.</param>
public record ActiveConditionDto(
    ConditionDto Condition,
    int RoundsLeft,
    string? SourceId);

/// <summary>A condition an ability applies (3B.4), for tooltips.</summary>
/// <param name="Duration">Full rounds it lasts after the round it lands in (0 = until the end of that round).</param>
/// <param name="RequiresHit">Only on a hit.</param>
public record AbilityConditionDto(
    ConditionDto Condition,
    int Duration,
    bool RequiresHit);

/// <summary>One attempt to put a condition on one target in a tick (3B.4).</summary>
/// <param name="AbilityId">The ability that applied it, or null for Defend's Guarded.</param>
/// <param name="Result">"applied", "refreshed", "resisted" or "wasted".</param>
/// <param name="RoundsLeft">Its rounds left afterwards (0 when wasted or resisted).</param>
/// <param name="WastedReason">Why nothing happened ("missed", "out_of_range", "target_defeated", …), or null.</param>
public record ConditionResult(
    string SourceId,
    string SourceName,
    string TargetId,
    string TargetName,
    string ConditionId,
    string ConditionName,
    bool Harmful,
    string? AbilityId,
    string Result,
    int RoundsLeft,
    string? WastedReason,
    string FeedText);

/// <summary>
/// One condition's periodic damage or healing at the end of the round (3B.4 PR 2). A combatant's periodic
/// effects are netted together, so <paramref name="NewHp"/> and <paramref name="Status"/> are the same on
/// each of its entries; only its last entry carries the HP and any fall in <paramref name="FeedText"/>.
/// </summary>
/// <param name="Kind">"damage" or "heal".</param>
/// <param name="Amount">HP lost (after the bearer's damage-taken modifiers) or healed.</param>
/// <param name="ConditionDamage">How much the bearer's damage-taken modifiers changed the damage, signed.</param>
/// <param name="Tags">The condition's tags (e.g. "flame"), for an icon.</param>
/// <param name="NewHp">HP after all of the combatant's periodic effects.</param>
/// <param name="Status">"standing", "downed" or "dead" afterwards.</param>
/// <param name="Fell">These effects made the combatant fall (Downed or dead).</param>
public record PeriodicResultDto(
    string CombatantId,
    string CombatantName,
    string ConditionId,
    string ConditionName,
    bool Harmful,
    IReadOnlyList<string> Tags,
    string Kind,
    int Amount,
    int ConditionDamage,
    int NewHp,
    string Status,
    bool Fell,
    string FeedText);

/// <summary>A condition that ran out at the end of the round (3B.4).</summary>
public record ExpiredConditionDto(
    string CombatantId,
    string CombatantName,
    string ConditionId,
    string ConditionName,
    string FeedText);
