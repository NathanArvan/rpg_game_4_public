namespace RpgGame.Rules;

/// <summary>
/// The simplest enemy AI (3B.0, AP queues since 3B.2, abilities since 3B.3). Each AI combatant
/// first heals an ally in trouble if it has a ready heal (ABILITIES.md A13 step 1), then spends the
/// rest of its AP on the nearest living enemy: attack it if it is in reach; otherwise walk toward it
/// (one move, up to the whole movement budget) and attack with the AP left if the walk ends in
/// reach. If the target isn't in reach but a ready Charge gets next to it, it charges instead of
/// walking (step 3). An attack opens with a ready multi-target ability if it would hit two or more
/// enemies (step 2), or else a ready single-target melee ability (step 4; one that would put a new
/// condition on the target comes first), then basic melee attacks with what's left. Right after
/// healing, it uses a ready condition ability (3B.4, SPRINT_3B4 C14): a buff if someone it would
/// reach doesn't have it yet, or a debuff on its target if the target doesn't have it yet. With
/// nothing to do, it defends. It plans within the AP and movement its conditions leave it (3B.4 PR 3); one
/// whose conditions stop it acting (Stunned, or 0 AP) isn't planned (the Api locks it in with an empty plan),
/// and a Rooted one doesn't walk or charge: it attacks if it's in reach, else defends. A walk that would leave a
/// standing enemy's reach (an opportunity attack, 3B.6) becomes a Disengage if the AP allows, else it attacks the
/// enemy next to it instead; a Charge that would provoke isn't used. A ready prepared reaction (3B.6 PR 2) ends the
/// queue instead of a lone Defend or leftover AP when its trigger could happen this round (<see cref="Finish"/>).
/// </summary>
/// <remarks>
/// <para>Pure and deterministic: same input, same plans. No dice, no I/O.</para>
/// <para>A combatant's team is its <see cref="CombatantState.Team"/>, or its entry in
/// <c>teamById</c> when that's empty; who is an ally or an enemy is then decided by
/// <see cref="Relations.Between"/>. Combatants with no team either way are ignored.</para>
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
    /// <returns>One action queue per living AI combatant, in <paramref name="aiCombatantIds"/> order.
    /// Every queue fits the combatant's AP budget.</returns>
    public static Dictionary<string, IReadOnlyList<PlannedAction>> PlanRound(
        EncounterState state,
        IReadOnlyDictionary<string, string> teamById,
        IReadOnlyList<string> aiCombatantIds,
        RulesConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(teamById);
        ArgumentNullException.ThrowIfNull(aiCombatantIds);
        config ??= RulesConfig.Default;

        // Everyone with a team filled in, so relations can be asked of Relations alone.
        state = state with
        {
            Combatants = state.Combatants
                .Select(c => c.Team.Length > 0 || !teamById.TryGetValue(c.Id, out var team) ? c : c with { Team = team })
                .ToList(),
        };
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
        var plans = new Dictionary<string, IReadOnlyList<PlannedAction>>();

        foreach (var id in aiCombatantIds)
        {
            if (!byId.TryGetValue(id, out var self) || self.IsDefeated) continue;
            if (self.Team.Length == 0) continue;
            if (ConditionMath.CannotAct(self.Profile, self.Conditions))
            {
                if (self.Position is { } stuck) blockers.Add(stuck);
                continue; // skips the round (SPRINT_3B4 C8)
            }
            // Plan within this round's budgets (C6).
            self = self with { Profile = ConditionMath.EffectiveProfile(self.Profile, self.Conditions) };
            bool rooted = ConditionMath.IsBlocked(self.Conditions, BlockKind.Move);

            // Not on the map (no position): it can't move, melee or reach anyone, so it just defends.
            if (self.Position is not { } from)
            {
                plans[id] = [PlannedAction.Defend()];
                continue;
            }

            // Step 1: heal an ally in trouble first (it resolves in tick 1, before any walk).
            var queue = new List<PlannedAction>();
            int apLeft = self.Profile.MaxAp;
            if (ChooseHeal(state, self, from, apLeft) is { } heal)
            {
                queue.Add(heal.Action);
                apLeft -= heal.Ability.ApCost;
            }

            var target = FindTarget(state, self, from);

            // Condition step (3B.4): a buff or a debuff, from where it stands, in tick 1.
            if (target is not null && ChooseConditionAbility(state, self, from, target, apLeft) is { } condition)
            {
                queue.Add(condition.Action);
                apLeft -= condition.Ability.ApCost;
            }

            if (target is null)
            {
                plans[id] = Finish(queue, state, self, from, config);
                blockers.Add(from);
                continue;
            }

            var targetPos = target.Position!.Value;
            if (from.ChebyshevDistanceTo(targetPos) <= config.MeleeReach)
            {
                AddAttacks(queue, state, self, from, target, apLeft, config);
                plans[id] = Finish(queue, state, self, from, config);
                blockers.Add(from);
                continue;
            }

            if (rooted)
            {
                plans[id] = Finish(queue, state, self, from, config); // can't close the distance
                blockers.Add(from);
                continue;
            }

            // Step 3: a ready Charge that gets next to the target closes and strikes in one tick. One that would run out
            // of a standing enemy's reach (an opportunity attack, 3B.6) is left to the walk below, which can Disengage.
            if (ChooseCharge(state, self, from, target, apLeft, config, occupied, blockers, reservedPaths) is { } charge
                && !Provokes(state, self, charge.Path, config))
            {
                var end = charge.Path[^1];
                reservedPaths.Add(charge.Path);
                occupied.Add(end);
                blockers.Add(end);
                queue.Add(PlannedAction.Ability(charge.Ability.Id, target.Id));
                AddAttacks(queue, state, self, end, target, apLeft - charge.Ability.ApCost, config);
                plans[id] = Finish(queue, state, self, end, config);
                continue;
            }

            var path = apLeft >= config.MoveApCost
                ? ChooseMove(state, from, targetPos, self.Profile.MovementRange, occupied, blockers, reservedPaths)
                : null;
            if (path is null)
            {
                plans[id] = Finish(queue, state, self, from, config); // boxed in (or out of AP)
                blockers.Add(from);
                continue;
            }

            // A walk out of a standing enemy's reach provokes an opportunity attack (3B.6, SPRINT_3B6 X15): Disengage
            // for it if the AP allows, else stay and fight the enemy next to it.
            bool disengage = false;
            if (Provokes(state, self, path, config))
            {
                if (apLeft >= config.DisengageApCost)
                    disengage = true;
                else if (AdjacentEnemy(state, self, from, config) is { } adjacent)
                {
                    AddAttacks(queue, state, self, from, adjacent, apLeft, config);
                    plans[id] = Finish(queue, state, self, from, config);
                    blockers.Add(from);
                    continue;
                }
            }

            var destination = path[^1];
            reservedPaths.Add(path);
            occupied.Add(destination);
            blockers.Add(destination);
            queue.Add(disengage
                ? PlannedAction.Disengage(destination.X, destination.Y)
                : PlannedAction.Move(destination.X, destination.Y));
            // Strike with what's left if the walk ends in reach (the target may still step away).
            if (destination.ChebyshevDistanceTo(targetPos) <= config.MeleeReach)
                AddAttacks(queue, state, self, destination, target,
                    apLeft - (disengage ? config.DisengageApCost : config.MoveApCost), config);
            plans[id] = Finish(queue, state, self, destination, config);
        }

        return plans;
    }

    /// <summary>
    /// The end of a queue (3B.6, SPRINT_3B6 X15): a ready prepared reaction the AP left pays for, whose trigger could
    /// plausibly happen this round from where the queue leaves the AI (<see cref="ChooseReaction"/>), goes last. Otherwise
    /// an empty queue defends.
    /// </summary>
    private static List<PlannedAction> Finish(
        List<PlannedAction> queue, EncounterState state, CombatantState self, GridPosition end, RulesConfig config)
    {
        int apLeft = self.Profile.MaxAp - PlanValidator.ApCost(queue, self.Profile, config);
        if (ChooseReaction(state, self, end, apLeft, config) is { } reaction)
            queue.Add(PlannedAction.Ability(reaction.Id));
        else if (queue.Count == 0)
            queue.Add(PlannedAction.Defend());
        return queue;
    }

    /// <summary>
    /// A ready, affordable prepared reaction worth watching with from <paramref name="at"/> (SPRINT_3B6 X15): Brace
    /// (<see cref="TriggerKind.EnemyEntersReach"/>) if a standing enemy outside its reach could walk into it this round
    /// (within that enemy's movement + reach); Overwatch (<see cref="TriggerKind.EnemyMovesWithin"/>) if a standing enemy
    /// that can move could end a step within its range. Judged from where everyone stands now; loadout order.
    /// </summary>
    private static AbilityDefinition? ChooseReaction(
        EncounterState state, CombatantState self, GridPosition at, int apLeft, RulesConfig config)
    {
        int reach = config.MeleeReach;
        var enemies = state.Combatants
            .Where(c => IsLivingEnemy(self, c))
            .Select(c => (Distance: at.ChebyshevDistanceTo(c.Position!.Value),
                          Move: ConditionMath.IsBlocked(c.Conditions, BlockKind.Move) ? 0 : ConditionMath.EffectiveMovementRange(c.Profile, c.Conditions)))
            .Where(e => e.Move > 0)
            .ToList();
        return self.Profile.Abilities.FirstOrDefault(a =>
            a is { IsPrepared: true, Trigger: { } trigger } && a.ApCost <= apLeft && self.CooldownLeft(a.Id) == 0
            && trigger.Kind switch
            {
                TriggerKind.EnemyEntersReach => enemies.Any(e => e.Distance > reach && e.Distance <= e.Move + reach),
                TriggerKind.EnemyMovesWithin => enemies.Any(e => e.Distance <= (trigger.Range ?? reach) + e.Move),
                _ => false, // attack triggers: 3B.6 PR 3
            });
    }

    /// <summary>
    /// Appends the attacks on an adjacent <paramref name="target"/> that <paramref name="apLeft"/> pays
    /// for, made from <paramref name="at"/>: a ready multi-target ability if it would hit two or more
    /// enemies from there (ABILITIES.md A13 step 2), a ready single-target melee ability (step 4), then
    /// as many basic melee attacks as the AP left covers.
    /// </summary>
    private static void AddAttacks(
        List<PlannedAction> queue, EncounterState state, CombatantState self, GridPosition at, CombatantState target,
        int apLeft, RulesConfig config)
    {
        if (AutoAttackAbility(state, self, at, apLeft) is { } multi)
        {
            queue.Add(PlannedAction.Ability(multi.Id));
            apLeft -= multi.ApCost;
        }
        if (MeleeAttackAbility(self, target, apLeft, config) is { } ability)
        {
            queue.Add(PlannedAction.Ability(ability.Id, target.Id));
            apLeft -= ability.ApCost;
        }
        AddMelees(queue, target.Id, apLeft, config);
    }

    /// <summary>
    /// Step 3 (ABILITIES.md A13): a ready ability that moves to its target and attacks it in melee
    /// (Charge), if <paramref name="target"/> is within its range and the walk to the free cell next to
    /// the target (found the way the resolver finds it) fits the move's cells (and the movement budget,
    /// if it uses it) along a clear path. The charger takes that path.
    /// </summary>
    private static (AbilityDefinition Ability, List<GridPosition> Path)? ChooseCharge(
        EncounterState state, CombatantState self, GridPosition from, CombatantState target, int apLeft, RulesConfig config,
        HashSet<GridPosition> occupied, HashSet<GridPosition> blockers, List<List<GridPosition>> reservedPaths)
    {
        var targetPos = target.Position!.Value;
        foreach (var ability in self.Profile.Abilities)
        {
            if (!Ready(self, ability, apLeft)
                || ability.Targeting is not { Kind: TargetKind.Combatant, Filter: TargetFilter.Enemy }
                || ability.Move is not { To: MoveTo.Target } move
                || !ability.Effects.Any(e => e is AttackEffect { Roll: AttackKind.Melee })
                || from.ChebyshevDistanceTo(targetPos) > ability.Targeting.Range)
                continue;

            if (RoundResolver.DestinationNear(from, targetPos, config.MeleeReach, state.GridWidth, state.GridHeight,
                    c => !occupied.Contains(c)) is not { } end || end == from)
                continue;
            int cap = move.UsesMovementBudget ? Math.Min(move.MaxCells, self.Profile.MovementRange) : move.MaxCells;
            if (from.ChebyshevDistanceTo(end) > cap) continue;

            var path = RoundResolver.ComputeChebyshevPath(from, end);
            if (PathIsClear(path, blockers, reservedPaths)) return (ability, path);
        }
        return null;
    }

    /// <summary>A ready, affordable, active ability of the loadout (not on cooldown).</summary>
    private static bool Ready(CombatantState self, AbilityDefinition a, int apLeft) =>
        a.Activation == AbilityActivation.Active && a.ApCost <= apLeft && self.CooldownLeft(a.Id) == 0;

    /// <summary>
    /// Step 1 (ABILITIES.md A13): a ready single-target heal on an ally (or itself, if the heal allows)
    /// within its range from where it stands now, who is Downed or Stable (if the heal can revive) or
    /// below half HP. Downed allies first, then the lowest share of HP, then state order.
    /// </summary>
    private static (AbilityDefinition Ability, PlannedAction Action)? ChooseHeal(
        EncounterState state, CombatantState self, GridPosition from, int apLeft)
    {
        foreach (var ability in self.Profile.Abilities)
        {
            if (!Ready(self, ability, apLeft) || ability.Targeting.Kind != TargetKind.Combatant) continue;
            if (ability.Effects.OfType<HealEffect>().FirstOrDefault() is not { } heal) continue;
            if (ability.Effects.Any(e => e is AttackEffect)) continue;

            var patient = state.Combatants
                .Select((c, index) => (c, index))
                .Where(x => x.c.Team.Length > 0
                            && x.c.Status != CombatantStatus.Dead
                            && Relations.Between(self, x.c) != Relation.Enemy
                            && Relations.Matches(self, x.c, ability.Targeting)
                            && x.c.Position is { } p && from.ChebyshevDistanceTo(p) <= ability.Targeting.Range
                            && NeedsHealing(x.c, ability.Targeting, heal))
                .OrderBy(x => x.c.Status == CombatantStatus.Downed ? 0 : 1)
                .ThenBy(x => (double)x.c.Hp / Math.Max(1, x.c.MaxHp))
                .ThenBy(x => x.index)
                .Select(x => x.c)
                .FirstOrDefault();
            if (patient is not null)
                return (ability, PlannedAction.Ability(ability.Id, patient.Id));
        }
        return null;
    }

    private static bool NeedsHealing(CombatantState c, Targeting targeting, HealEffect heal) => c.Status switch
    {
        CombatantStatus.Downed => heal.CanRevive || targeting.Filter == TargetFilter.DownedAlly,
        CombatantStatus.Stable => heal.CanRevive && targeting.Filter != TargetFilter.DownedAlly,
        CombatantStatus.Standing => targeting.Filter != TargetFilter.DownedAlly && c.Hp * 2 < c.MaxHp,
        _ => false,
    };

    /// <summary>
    /// Step 2 (ABILITIES.md A13): a ready <see cref="TargetKind.Auto"/> attack ability on enemies that
    /// would catch at least two standing enemies within its range of <paramref name="at"/>.
    /// </summary>
    private static AbilityDefinition? AutoAttackAbility(EncounterState state, CombatantState self, GridPosition at, int apLeft) =>
        self.Profile.Abilities.FirstOrDefault(a =>
            Ready(self, a, apLeft)
            && a.Targeting is { Kind: TargetKind.Auto, Filter: TargetFilter.Enemy }
            && a.Effects.Count > 0
            && a.Effects.All(e => e is AttackEffect)
            && state.Combatants.Count(c => IsLivingEnemy(self, c)
                                           && at.ChebyshevDistanceTo(c.Position!.Value) <= a.Targeting.Range) >= 2);

    /// <summary>
    /// The first loadout ability that is a single-target melee attack on an enemy (possibly with
    /// conditions on hit), ready (no cooldown) and affordable. Abilities count as stronger than a basic
    /// swing; one that would put a condition on <paramref name="target"/> that it doesn't have yet comes
    /// first (3B.4).
    /// </summary>
    private static AbilityDefinition? MeleeAttackAbility(CombatantState self, CombatantState target, int apLeft, RulesConfig config) =>
        self.Profile.Abilities
            .Where(a => Ready(self, a, apLeft)
                        && a.Targeting is { Kind: TargetKind.Combatant, Filter: TargetFilter.Enemy }
                        && a.Targeting.Range >= config.MeleeReach
                        && a.Effects.Any(e => e is AttackEffect)
                        && a.Effects.All(e => e is AttackEffect { Roll: AttackKind.Melee } or ApplyConditionEffect))
            .OrderBy(a => AddsCondition(a, target) ? 0 : 1) // stable: otherwise loadout order
            .FirstOrDefault();

    /// <summary>Whether the ability would put at least one condition on <paramref name="c"/> that it doesn't have.</summary>
    private static bool AddsCondition(AbilityDefinition a, CombatantState c) =>
        a.ConditionEffects.Any(e => !c.HasCondition(e.Condition.Id));

    /// <summary>
    /// The condition step (3B.4, SPRINT_3B4 C14): the first ready ability that only applies conditions and
    /// would add one someone doesn't have yet, from where the AI stands. Buffs (no harmful condition) go on
    /// itself or allies it reaches: for a <see cref="TargetKind.Self"/> or <see cref="TargetKind.Auto"/> one,
    /// if anyone it would affect lacks it; for a chosen ally, itself first, then allies in state order.
    /// Debuffs (all harmful) go on <paramref name="target"/>, the enemy it's after, if it's in range and lacks one.
    /// </summary>
    private static (AbilityDefinition Ability, PlannedAction Action)? ChooseConditionAbility(
        EncounterState state, CombatantState self, GridPosition from, CombatantState target, int apLeft)
    {
        foreach (var ability in self.Profile.Abilities)
        {
            if (!Ready(self, ability, apLeft) || ability.Effects.Count == 0
                || !ability.Effects.All(e => e is ApplyConditionEffect))
                continue;
            var t = ability.Targeting;
            bool InRange(CombatantState c) =>
                c.Id == self.Id || (c.Position is { } p && from.ChebyshevDistanceTo(p) <= t.Range);

            if (ability.ConditionEffects.All(e => e.Condition.Harmful))
            {
                if (t is { Kind: TargetKind.Combatant, Filter: TargetFilter.Enemy } && InRange(target) && AddsCondition(ability, target))
                    return (ability, PlannedAction.Ability(ability.Id, target.Id));
                continue;
            }
            if (ability.ConditionEffects.Any(e => e.Condition.Harmful)) continue; // mixed: not used by the AI

            var allies = state.Combatants
                .Where(c => !c.IsDefeated && c.Team.Length > 0 && Relations.Matches(self, c, t) && InRange(c))
                .OrderBy(c => c.Id == self.Id ? 0 : 1) // stable: then state order
                .ToList();
            switch (t.Kind)
            {
                case TargetKind.Self when AddsCondition(ability, self):
                    return (ability, PlannedAction.Ability(ability.Id));
                case TargetKind.Auto when t.Filter != TargetFilter.Enemy && allies.Any(c => AddsCondition(ability, c)):
                    return (ability, PlannedAction.Ability(ability.Id));
                case TargetKind.Combatant when t.Filter is TargetFilter.Ally or TargetFilter.Any:
                    if (allies.FirstOrDefault(c => AddsCondition(ability, c)) is { } ally)
                        return (ability, PlannedAction.Ability(ability.Id, ally.Id));
                    break;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether walking <paramref name="path"/> would provoke an opportunity attack (3B.6, SPRINT_3B6 X2, X5), judged
    /// from where everyone stands now: some step leaves the melee reach of a standing enemy that makes opportunity
    /// attacks and isn't Stunned. It doesn't guess whether that enemy will be moving itself.
    /// </summary>
    private static bool Provokes(EncounterState state, CombatantState self, List<GridPosition> path, RulesConfig config)
    {
        int reach = config.MeleeReach;
        var reactors = state.Combatants
            .Where(c => !c.IsDefeated && c.Position.HasValue && c.Profile.OpportunityAttacks
                        && Relations.Between(self, c) == Relation.Enemy
                        && !ConditionMath.IsBlocked(c.Conditions, BlockKind.All))
            .Select(c => c.Position!.Value)
            .ToList();
        for (int step = 1; step < path.Count; step++)
            foreach (var at in reactors)
                if (at.ChebyshevDistanceTo(path[step - 1]) <= reach && at.ChebyshevDistanceTo(path[step]) > reach)
                    return true;
        return false;
    }

    /// <summary>A standing enemy within melee reach of <paramref name="at"/> (state order), or null.</summary>
    private static CombatantState? AdjacentEnemy(EncounterState state, CombatantState self, GridPosition at, RulesConfig config) =>
        state.Combatants.FirstOrDefault(c => !c.IsDefeated && c.Position is { } p && Relations.Between(self, c) == Relation.Enemy
                                             && at.ChebyshevDistanceTo(p) <= config.MeleeReach);

    /// <summary>Appends as many melee attacks on <paramref name="targetId"/> as <paramref name="apLeft"/> pays for.</summary>
    private static void AddMelees(List<PlannedAction> queue, string targetId, int apLeft, RulesConfig config)
    {
        if (config.MeleeApCost <= 0) { queue.Add(PlannedAction.Melee(targetId)); return; }
        for (; apLeft >= config.MeleeApCost; apLeft -= config.MeleeApCost)
            queue.Add(PlannedAction.Melee(targetId));
    }

    /// <summary>Nearest living enemy on the map; ties go to the lowest HP, then to state order.</summary>
    private static CombatantState? FindTarget(EncounterState state, CombatantState self, GridPosition from)
    {
        return state.Combatants
            .Select((c, index) => (c, index))
            .Where(x => IsLivingEnemy(self, x.c))
            .OrderBy(x => from.ChebyshevDistanceTo(x.c.Position!.Value))
            .ThenBy(x => x.c.Hp)
            .ThenBy(x => x.index)
            .Select(x => x.c)
            .FirstOrDefault();
    }

    /// <summary>A standing enemy on the map with a known team (the AI never goes after someone already down).</summary>
    private static bool IsLivingEnemy(CombatantState self, CombatantState c) =>
        !c.IsDefeated && c.Position.HasValue && c.Team.Length > 0 && Relations.Between(self, c) == Relation.Enemy;

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
