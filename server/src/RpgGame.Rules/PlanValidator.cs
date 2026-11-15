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
/// </summary>
/// <remarks>
/// Checks only budgets and the grid. Whether attack targets exist or are already defeated is the
/// caller's job (the Api knows; the resolver wastes such attacks).
/// </remarks>
public static class PlanValidator
{
    public static PlanCheck Validate(
        GridPosition? start,
        CombatProfile profile,
        IReadOnlyList<PlannedAction> queue,
        int gridWidth,
        int gridHeight,
        RulesConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queue);
        config ??= RulesConfig.Default;

        int ap = 0, cells = 0;
        var position = start;
        foreach (var action in queue)
        {
            ap += config.ApCost(action.Type);
            if (ap > profile.MaxAp)
                return new PlanCheck($"Not enough AP: this plan costs {ApCost(queue, config)} AP but only {profile.MaxAp} AP are available.", ap, cells);

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

    /// <summary>Total AP cost of a queue.</summary>
    public static int ApCost(IReadOnlyList<PlannedAction> queue, RulesConfig? config = null)
    {
        config ??= RulesConfig.Default;
        return queue.Sum(a => config.ApCost(a.Type));
    }

    /// <summary>How many actions from the front of <paramref name="queue"/> fit within <paramref name="maxAp"/>.</summary>
    public static int AffordableCount(IReadOnlyList<PlannedAction> queue, int maxAp, RulesConfig? config = null)
    {
        config ??= RulesConfig.Default;
        int ap = 0;
        for (int i = 0; i < queue.Count; i++)
        {
            ap += config.ApCost(queue[i].Type);
            if (ap > maxAp) return i;
        }
        return queue.Count;
    }
}
