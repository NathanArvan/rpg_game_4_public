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
/// Disengage (3B.6) counts as a move for the budget, the route and Rooted. A prepared reaction (3B.6, SPRINT_3B6 X7)
/// takes no target and must be the last action of the queue.
/// Conditions (3B.4 PR 3, SPRINT_3B4 C6, C8) change the AP and movement budgets, reject any plan while
/// they block every action (Stunned), and reject moves and abilities that move while they block moves (Rooted).
/// </summary>
/// <remarks>
/// Checks only budgets, the loadout and the grid. Whether attack targets exist, are already
/// defeated or are on the right side is the caller's job (the Api knows; the resolver wastes such
/// actions). Range is checked at resolution, because positions change during the round.
/// </remarks>
public static class PlanValidator
{
    /// <param name="cooldowns">Abilities on cooldown at the start of this planning phase (id → rounds left).</param>
    /// <param name="positions">
    /// Where everyone stands at the start of planning, to estimate a move to a target (Charge). Without
    /// it (or for an unknown target) such a move is counted as zero cells.
    /// </param>
    /// <param name="conditions">The combatant's conditions at the start of this planning phase (3B.4): they set the
    /// effective budgets and what it can't do.</param>
    public static PlanCheck Validate(
        GridPosition? start,
        CombatProfile profile,
        IReadOnlyList<PlannedAction> queue,
        int gridWidth,
        int gridHeight,
        RulesConfig? config = null,
        IReadOnlyDictionary<string, int>? cooldowns = null,
        IReadOnlyDictionary<string, GridPosition>? positions = null,
        IReadOnlyList<ActiveCondition>? conditions = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queue);
        config ??= RulesConfig.Default;
        conditions ??= [];
        profile = ConditionMath.EffectiveProfile(profile, conditions);

        if (queue.Count > 0 && ConditionMath.BlockingCondition(conditions, BlockKind.All) is { } stun)
            return new PlanCheck($"This combatant is {stun.Definition.Name} and can't act this round.", 0, 0);
        var root = ConditionMath.BlockingCondition(conditions, BlockKind.Move);

        int ap = 0, cells = 0;
        var position = start;
        var abilitiesQueued = new HashSet<string>();
        bool preparedQueued = false;
        foreach (var action in queue)
        {
            if (preparedQueued)
                return new PlanCheck(PreparedNotLast, ap, cells);
            preparedQueued = action.Type == ActionType.Ability && profile.FindAbility(action.AbilityId) is { IsPrepared: true };

            if (action.Type == ActionType.Ability
                && CheckAbility(action, profile, cooldowns, abilitiesQueued) is { } abilityError)
                return new PlanCheck(abilityError, ap, cells);

            if (root is not null
                && (action.Type is ActionType.Move or ActionType.Disengage || profile.FindAbility(action.AbilityId) is { Move: not null }))
                return new PlanCheck($"This combatant is {root.Definition.Name} and can't move.", ap, cells);

            ap += config.ApCost(action, profile);
            if (ap > profile.MaxAp)
                return new PlanCheck($"Not enough AP: this plan costs {ApCost(queue, profile, config)} AP but only {profile.MaxAp} AP are available.", ap, cells);

            if (action.Type == ActionType.Ability && profile.FindAbility(action.AbilityId) is { Move: { } move } moving)
            {
                if (CheckAbilityMove(action, moving, move, profile, ref position, ref cells, gridWidth, gridHeight, config, positions)
                    is { } moveError)
                    return new PlanCheck(moveError, ap, cells);
                continue;
            }

            // A Disengage is a move for the budget and the route (SPRINT_3B6 X4).
            if (action.Type is not (ActionType.Move or ActionType.Disengage)) continue;

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

    /// <summary>The X7 rule's message (SPRINT_3B6): nothing can follow a prepared reaction.</summary>
    public const string PreparedNotLast = "A prepared reaction must be the last action in the queue.";

    /// <summary>
    /// An ability's move effect (ABILITIES.md A7). A move to a cell must stay on the grid and within
    /// the effect's <see cref="MoveEffect.MaxCells"/> of where the earlier moves end. A move to a target
    /// is estimated from the target's position at the start of planning: toward it until in melee
    /// reach, capped at <see cref="MoveEffect.MaxCells"/> and the budget left (the resolver caps it the
    /// same way, so it's never rejected for distance). Budget moves add their cells to the round's
    /// total; a cell move that would go over it is rejected, like a plain move.
    /// </summary>
    private static string? CheckAbilityMove(
        PlannedAction action, AbilityDefinition ability, MoveEffect move, CombatProfile profile,
        ref GridPosition? position, ref int cells, int gridWidth, int gridHeight, RulesConfig config,
        IReadOnlyDictionary<string, GridPosition>? positions)
    {
        if (position is not { } from)
            return "Current position not found.";

        int steps;
        GridPosition end;
        if (move.To == MoveTo.Cell)
        {
            if (action.TargetCell is not { } target)
                return $"{ability.Name} needs a target cell.";
            if (target.X < 0 || target.Y < 0 || target.X >= gridWidth || target.Y >= gridHeight)
                return $"Target cell is out of bounds (0–{gridWidth - 1}, 0–{gridHeight - 1}).";
            steps = from.ChebyshevDistanceTo(target);
            if (steps > move.MaxCells)
                return $"{ability.Name} moves at most {move.MaxCells} {(move.MaxCells == 1 ? "square" : "squares")}.";
            if (move.UsesMovementBudget && cells + steps > profile.MovementRange)
                return $"Moves this round exceed maximum range ({profile.MovementRange} squares).";
            end = target;
        }
        else
        {
            if (action.TargetCombatantId is not { } targetId || positions?.TryGetValue(targetId, out var targetAt) != true)
                return null;
            int cap = move.MaxCells;
            if (move.UsesMovementBudget) cap = Math.Min(cap, Math.Max(0, profile.MovementRange - cells));
            steps = Math.Min(cap, Math.Max(0, from.ChebyshevDistanceTo(targetAt) - config.MeleeReach));
            var path = RoundResolver.ComputeChebyshevPath(from, targetAt);
            end = path[Math.Min(steps, path.Count - 1)];
        }

        if (move.UsesMovementBudget) cells += steps;
        position = end;
        return null;
    }

    /// <summary>Loadout, cooldown, once-per-queue and target-shape checks for one ability entry.</summary>
    private static string? CheckAbility(
        PlannedAction action, CombatProfile profile, IReadOnlyDictionary<string, int>? cooldowns, HashSet<string> queued)
    {
        var ability = profile.FindAbility(action.AbilityId);
        if (ability is null)
            return $"This combatant doesn't have the ability '{action.AbilityId}'.";
        if (ability.Activation == AbilityActivation.Passive || (ability.IsPrepared && ability.Trigger is null))
            return $"{ability.Name} can't be queued.";

        int left = cooldowns?.GetValueOrDefault(ability.Id) ?? 0;
        if (left > 0)
            return $"{ability.Name} is on cooldown for {left} more {(left == 1 ? "round" : "rounds")}.";
        if (ability.Cooldown > 0 && !queued.Add(ability.Id))
            return $"{ability.Name} can only be used once per round.";

        bool hasOne = !string.IsNullOrEmpty(action.TargetCombatantId);
        int listed = action.TargetCombatantIds?.Count ?? 0;
        if (action.TargetCell is not null && !ability.NeedsCell)
            return $"{ability.Name} doesn't take a target cell.";
        return ability.Targeting.Kind switch
        {
            TargetKind.Combatant when !hasOne || listed > 0 => $"{ability.Name} needs one target.",
            TargetKind.Self or TargetKind.Auto or TargetKind.Trigger when hasOne || listed > 0 => $"{ability.Name} doesn't take a target.",
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
