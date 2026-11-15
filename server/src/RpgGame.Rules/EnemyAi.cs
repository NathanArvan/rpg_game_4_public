namespace RpgGame.Rules;

/// <summary>
/// The simplest enemy AI (3B.0): each AI combatant picks one action for the round. Melee an
/// adjacent enemy if there is one; otherwise move toward the nearest living enemy.
/// </summary>
/// <remarks>
/// <para>Pure and deterministic: same input, same plans. No dice, no I/O.</para>
/// <para>Teams are passed in rather than read from <see cref="CombatantState"/>, so the resolver and
/// existing scenarios don't depend on them. Any two different team strings are enemies.</para>
/// <para>AI combatants plan in order and avoid the walks already chosen by earlier ones (same cell
/// at the same step would block both). They don't predict where players will move, and an earlier
/// walker may still bump into a later AI combatant that ends up standing still; those cases are left
/// to the normal movement rules. Fleeing, focus fire and vision come later (3D.1+).</para>
/// </remarks>
public static class EnemyAi
{
    /// <param name="state">Encounter state at the start of planning.</param>
    /// <param name="teamById">Team of every combatant. Combatants missing from it are ignored as targets.</param>
    /// <param name="aiCombatantIds">Combatants to plan for, in priority order (earlier ones claim cells first).</param>
    /// <returns>One action per living AI combatant, in <paramref name="aiCombatantIds"/> order.</returns>
    public static Dictionary<string, PlannedAction> PlanRound(
        EncounterState state,
        IReadOnlyDictionary<string, string> teamById,
        IReadOnlyList<string> aiCombatantIds,
        RulesConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(teamById);
        ArgumentNullException.ThrowIfNull(aiCombatantIds);
        config ??= RulesConfig.Default;

        var byId = state.Combatants.ToDictionary(c => c.Id);
        var aiSet = aiCombatantIds.ToHashSet();

        // Cells held by anyone who won't be walking away this round: everyone except AI combatants.
        // (AI combatants are added back below if they end up not moving.) Defeated bodies block too,
        // as in the resolver. Players might move away, but the AI doesn't try to predict that.
        var blockers = state.Combatants
            .Where(c => c.Position.HasValue && (!aiSet.Contains(c.Id) || c.IsDefeated))
            .Select(c => c.Position!.Value)
            .ToHashSet();
        var occupied = state.Combatants
            .Where(c => c.Position.HasValue)
            .Select(c => c.Position!.Value)
            .ToHashSet();
        var reservedPaths = new List<List<GridPosition>>();
        var plans = new Dictionary<string, PlannedAction>();

        foreach (var id in aiCombatantIds)
        {
            if (!byId.TryGetValue(id, out var self) || self.IsDefeated) continue;
            if (!teamById.TryGetValue(id, out var myTeam)) continue;

            // Not on the map (no position): it can't move or melee, so it just defends.
            if (self.Position is not { } from)
            {
                plans[id] = PlannedAction.Defend();
                continue;
            }

            var target = FindTarget(state, self, from, myTeam, teamById);
            if (target is null)
            {
                plans[id] = PlannedAction.Defend();
                blockers.Add(from);
                continue;
            }

            var targetPos = target.Position!.Value;
            if (from.ChebyshevDistanceTo(targetPos) <= config.MeleeReach)
            {
                plans[id] = PlannedAction.Melee(target.Id);
                blockers.Add(from);
                continue;
            }

            var path = ChooseMove(state, from, targetPos, self.Profile.MovementRange, occupied, blockers, reservedPaths);
            if (path is null)
            {
                plans[id] = PlannedAction.Defend(); // boxed in
                blockers.Add(from);
                continue;
            }

            var destination = path[^1];
            reservedPaths.Add(path);
            occupied.Add(destination);
            blockers.Add(destination);
            plans[id] = PlannedAction.Move(destination.X, destination.Y);
        }

        return plans;
    }

    /// <summary>Nearest living enemy on the map; ties go to the lowest HP, then to state order.</summary>
    private static CombatantState? FindTarget(
        EncounterState state, CombatantState self, GridPosition from, string myTeam, IReadOnlyDictionary<string, string> teamById)
    {
        return state.Combatants
            .Select((c, index) => (c, index))
            .Where(x => !x.c.IsDefeated
                        && x.c.Id != self.Id
                        && x.c.Position.HasValue
                        && teamById.TryGetValue(x.c.Id, out var team)
                        && team != myTeam)
            .OrderBy(x => from.ChebyshevDistanceTo(x.c.Position!.Value))
            .ThenBy(x => x.c.Hp)
            .ThenBy(x => x.index)
            .Select(x => x.c)
            .FirstOrDefault();
    }

    /// <summary>
    /// Picks where to walk: the free cell within the walker's movement range that is closest to the target
    /// (an adjacent cell when one is reachable). Ties prefer cells straight in line with the target,
    /// then a shorter walk, then top-to-bottom, left-to-right. A cell is skipped if the walk there
    /// would pass through a blocked cell, or step onto the same cell at the same moment as another
    /// AI's walk (equal Speed would stop both).
    /// </summary>
    /// <returns>The full path including the start cell, or null if no free cell is in range.</returns>
    private static List<GridPosition>? ChooseMove(
        EncounterState state,
        GridPosition from,
        GridPosition target,
        int range,
        HashSet<GridPosition> occupied,
        HashSet<GridPosition> blockers,
        List<List<GridPosition>> reservedPaths)
    {
        var candidates = new List<GridPosition>();
        for (int y = Math.Max(0, from.Y - range); y <= Math.Min(state.GridHeight - 1, from.Y + range); y++)
            for (int x = Math.Max(0, from.X - range); x <= Math.Min(state.GridWidth - 1, from.X + range); x++)
            {
                var cell = new GridPosition(x, y);
                if (cell != from && !occupied.Contains(cell)) candidates.Add(cell);
            }

        var ordered = candidates
            .OrderBy(c => c.ChebyshevDistanceTo(target))
            .ThenBy(c => Math.Abs(c.X - target.X) + Math.Abs(c.Y - target.Y))
            .ThenBy(c => from.ChebyshevDistanceTo(c))
            .ThenBy(c => c.Y)
            .ThenBy(c => c.X)
            .ToList();

        // Never walk away from the target: only cells that get closer than standing still.
        int standingDistance = from.ChebyshevDistanceTo(target);
        ordered = ordered.Where(c => c.ChebyshevDistanceTo(target) < standingDistance).ToList();

        foreach (var cell in ordered)
        {
            var path = RoundResolver.ComputeChebyshevPath(from, cell);
            if (PathIsClear(path, blockers, reservedPaths)) return path;
        }

        // Nothing clear: take the best cell anyway and let the movement rules sort it out.
        return ordered.Count > 0 ? RoundResolver.ComputeChebyshevPath(from, ordered[0]) : null;
    }

    private static bool PathIsClear(List<GridPosition> path, HashSet<GridPosition> blockers, List<List<GridPosition>> reservedPaths)
    {
        for (int step = 1; step < path.Count; step++)
        {
            if (blockers.Contains(path[step])) return false;
            foreach (var other in reservedPaths)
            {
                // Where the other walker stands at this step (it waits at its destination once done).
                var theirs = other[Math.Min(step, other.Count - 1)];
                if (theirs == path[step]) return false;
            }
        }
        return true;
    }
}
