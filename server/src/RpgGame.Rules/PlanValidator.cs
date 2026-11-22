namespace RpgGame.Rules;

/// <summary>Result of <see cref="PlanValidator.Validate"/>.</summary>
/// <param name="Error">Null when the queue is legal; otherwise why not (shown to the player).</param>
/// <param name="ApSpent">AP the whole queue costs.</param>
/// <param name="MoveCellsUsed">Cells the queue's moves cover, measured along the projected route.</param>
public sealed record PlanCheck(string? Error, int ApSpent, int MoveCellsUsed)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Budget rules for one combatant's action queue (roadmap 3B.2): the queue's total AP cost must
/// fit <see cref="CombatProfile.MaxAp"/>, and all of its moves together may cover at most
/// <see cref="CombatProfile.MovementRange"/> cells. Each move is measured from where the earlier
/// queued moves would leave the combatant, ignoring everyone else (who may block it at resolution).
/// Ability entries (3B.3) must name a loadout ability that isn't on cooldown, appear at most once
/// if the ability has a cooldown, and carry the target shape its targeting kind needs.
/// </summary>
/// <remarks>
/// Checks only budgets, the loadout and the grid. Whether attack targets exist, are already
/// defeated or are on the right side is the caller's job (the Api knows; the resolver wastes such
/// actions). Range is checked at resolution, because positions change during the round.
/// </remarks>
public static class PlanValidator
{
    /// <param name="cooldowns">Abilities on cooldown at the start of this planning phase (id → rounds left).</param>
    public static PlanCheck Validate(
        GridPosition? start,
        CombatProfile profile,
        IReadOnlyList<PlannedAction> queue,
        int gridWidth,
        int gridHeight,
        RulesConfig? config = null,
        IReadOnlyDictionary<string, int>? cooldowns = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queue);
        config ??= RulesConfig.Default;

        int ap = 0, cells = 0;
        var position = start;
        var abilitiesQueued = new HashSet<string>();
        foreach (var action in queue)
        {
            if (action.Type == ActionType.Ability
                && CheckAbility(action, profile, cooldowns, abilitiesQueued) is { } abilityError)
                return new PlanCheck(abilityError, ap, cells);

            ap += config.ApCost(action, profile);
            if (ap > profile.MaxAp)
                return new PlanCheck($"Not enough AP: this plan costs {ApCost(queue, profile, config)} AP but only {profile.MaxAp} AP are available.", ap, cells);

            if (action.Type != ActionType.Move) continue;

            if (action.TargetCell is not { } target)
                return new PlanCheck("Move action requires a target cell.", ap, cells);
            if (target.X < 0 || target.Y < 0 || target.X >= gridWidth || target.Y >= gridHeight)
                return new PlanCheck($"Target cell is out of bounds (0–{gridWidth - 1}, 0–{gridHeight - 1}).", ap, cells);
            if (position is not { } from)
                return new PlanCheck("Current position not found.", ap, cells);

            cells += from.ChebyshevDistanceTo(target);
            if (cells > profile.MovementRange)
                return new PlanCheck($"Moves this round exceed maximum range ({profile.MovementRange} squares).", ap, cells);
            position = target;
        }

        return new PlanCheck(null, ap, cells);
    }

    /// <summary>Loadout, cooldown, once-per-queue and target-shape checks for one ability entry.</summary>
    private static string? CheckAbility(
        PlannedAction action, CombatProfile profile, IReadOnlyDictionary<string, int>? cooldowns, HashSet<string> queued)
    {
        var ability = profile.FindAbility(action.AbilityId);
        if (ability is null)
            return $"This combatant doesn't have the ability '{action.AbilityId}'.";
        if (ability.Activation != AbilityActivation.Active)
            return $"{ability.Name} can't be queued.";

        int left = cooldowns?.GetValueOrDefault(ability.Id) ?? 0;
        if (left > 0)
            return $"{ability.Name} is on cooldown for {left} more {(left == 1 ? "round" : "rounds")}.";
        if (ability.Cooldown > 0 && !queued.Add(ability.Id))
            return $"{ability.Name} can only be used once per round.";

        bool hasOne = !string.IsNullOrEmpty(action.TargetCombatantId);
        int listed = action.TargetCombatantIds?.Count ?? 0;
        return ability.Targeting.Kind switch
        {
            TargetKind.Combatant when !hasOne || listed > 0 => $"{ability.Name} needs one target.",
            TargetKind.Self or TargetKind.Auto when hasOne || listed > 0 => $"{ability.Name} doesn't take a target.",
            TargetKind.Combatants when hasOne || listed == 0
                => $"{ability.Name} needs between 1 and {ability.Targeting.Count ?? 1} targets.",
            TargetKind.Combatants when listed > (ability.Targeting.Count ?? 1)
                => $"{ability.Name} can target at most {ability.Targeting.Count ?? 1}.",
            TargetKind.Combatants when action.TargetCombatantIds!.Distinct().Count() != listed
                => $"{ability.Name} can't target the same combatant twice.",
            TargetKind.Cell => $"{ability.Name} targets a cell, which isn't supported yet.",
            _ => null,
        };
    }

    /// <summary>Total AP cost of a queue.</summary>
    public static int ApCost(IReadOnlyList<PlannedAction> queue, CombatProfile profile, RulesConfig? config = null)
    {
        config ??= RulesConfig.Default;
        return queue.Sum(a => config.ApCost(a, profile));
    }

    /// <summary>How many actions from the front of <paramref name="queue"/> fit within <paramref name="profile"/>'s MaxAp.</summary>
    public static int AffordableCount(IReadOnlyList<PlannedAction> queue, CombatProfile profile, RulesConfig? config = null)
    {
        config ??= RulesConfig.Default;
        int ap = 0;
        for (int i = 0; i < queue.Count; i++)
        {
            ap += config.ApCost(queue[i], profile);
            if (ap > profile.MaxAp) return i;
        }
        return queue.Count;
    }
}
