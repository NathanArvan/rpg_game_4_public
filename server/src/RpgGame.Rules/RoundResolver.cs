namespace RpgGame.Rules;

/// <summary>
/// Resolves one round of simultaneously planned actions using the tick model
/// (docs/design/TICK_RESOLUTION.md): one tick per queue position, and within each tick
/// Movement → Attacks → Effects (DESIGN_SPIKE Decision 1).
/// </summary>
/// <remarks>
/// <para>Pure: never mutates its inputs and does no I/O, logging or clock access. All randomness
/// comes from the supplied <see cref="IDiceRoller"/>.</para>
/// <para><b>Plan order matters.</b> <c>plans</c> is enumerated in its given order, which decides
/// the order dice are rolled, the order of results within a tick, and who is reported as the
/// blocker when tied-speed movers contest a cell. Pass an insertion-ordered dictionary such as <see cref="Dictionary{TKey,TValue}"/>.</para>
/// <para>Comments tagged "Quirk N" mark current behavior that is preserved deliberately but may
/// not match the design docs; see the refactor/extract-rules-engine PR for the list.</para>
/// </remarks>
public static class RoundResolver
{
    public static RoundOutcome Resolve(
        EncounterState state,
        IReadOnlyDictionary<string, IReadOnlyList<PlannedAction>> plans,
        IDiceRoller dice,
        RulesConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(dice);
        config ??= RulesConfig.Default;

        var round = new RoundContext(state, plans, dice, config);

        // Ticks = longest (affordable) queue. Always at least one tick, so a round with no plans
        // still produces one (empty) tick.
        int tickCount = Math.Max(1, round.Plans.Count == 0 ? 0 : round.Plans.Values.Max(q => q.Count));

        var ticks = new List<TickOutcome>(tickCount);
        for (int tick = 0; tick < tickCount; tick++)
        {
            // Combatants defeated in an earlier tick are skipped (CanAct), which voids
            // their remaining queued actions.
            // A combatant a condition stops from acting (Stunned) loses this tick's action (C8).
            ResolveBlocks(round, tick);
            ResolveAbilityActivations(round, tick);
            ResolveGuards(round, tick);
            var movements = ResolveMovementPhase(round, tick);
            var (attacks, heals) = ResolveAttackPhase(round, tick);
            var effects = ResolveEffectsPhase(round, tick);
            ticks.Add(new TickOutcome(tick, movements, attacks, effects)
            {
                AbilityUses = round.TickUses.ToList(),
                Heals = heals,
                Conditions = round.TickConditions.ToList(),
                Blocked = round.TickBlocked.ToList(),
                Reactions = round.TickReactions.ToList(),
                Watching = round.TickWatching.ToList(),
            });
        }

        // Prepared reactions still watching never fired (SPRINT_3B6 X10): reported, no refund.
        var unused = state.Combatants
            .Where(c => round.Watching.ContainsKey(c.Id))
            .Select(c => new PreparedReaction(c.Id, round.Watching[c.Id].Id))
            .ToList();

        // End of round (SPRINT_3B4 C11): periodic effects (and anyone they drop) → bleedout → cooldowns →
        // conditions count down and expire.
        var periodic = ResolvePeriodic(round);
        var endOfRound = ResolveBleedout(round);
        var cooldowns = ResolveCooldowns(round);
        var (conditions, expired) = ResolveConditionCountdown(round);

        var next = state with
        {
            Combatants = state.Combatants.Select(c => c with
            {
                Hp = round.Hp[c.Id],
                Position = round.Positions.TryGetValue(c.Id, out var p) ? p : c.Position,
                Status = round.Status[c.Id],
                BleedoutRoundsLeft = round.Status[c.Id] == CombatantStatus.Downed ? round.Bleedout[c.Id] : 0,
                Cooldowns = cooldowns[c.Id],
                Conditions = conditions[c.Id],
            }).ToList(),
        };

        // Net change over the round: someone who went down and was healed back up is in neither list.
        var defeated = round.DefeatedThisRound.Distinct().Where(round.IsOut).ToList();
        var revived = state.Combatants.Where(c => c.IsDefeated && !round.IsOut(c.Id)).Select(c => c.Id).ToList();
        return new RoundOutcome(next, ticks, defeated, round.Plans, endOfRound)
        {
            RevivedThisRound = revived,
            ExpiredConditions = expired,
            PeriodicEffects = periodic,
            UnusedReactions = unused,
        };
    }

    // ----- Blocked actions (start of each tick) -----

    /// <summary>
    /// SPRINT_3B4 C8: a combatant whose conditions block every action (Stunned), from the start of the round or
    /// from an earlier tick, doesn't act this tick. Its queued action is reported once, in
    /// <see cref="TickOutcome.Blocked"/>, and doesn't resolve: a blocked ability isn't used, so no cooldown.
    /// Conditions applied in this tick's Effects phase count from the next tick.
    /// </summary>
    private static void ResolveBlocks(RoundContext round, int tick)
    {
        round.TickStunned.Clear();
        round.TickBlocked.Clear();
        // A watcher a condition now stops from acting (Stunned in an earlier tick's Effects phase) stops watching
        // from this tick on (SPRINT_3B6 X8).
        foreach (var watcherId in round.Watching.Keys.ToList())
            if (ConditionMath.IsBlocked(round.Conditions[watcherId], BlockKind.All))
                round.Watching.Remove(watcherId);
        foreach (var (combatantId, queue) in round.Plans)
        {
            if (!round.CanAct(combatantId) || ActionAt(queue, tick) is not { } action) continue;
            if (ConditionMath.BlockingCondition(round.Conditions[combatantId], BlockKind.All) is not { } stun) continue;
            round.TickStunned.Add(combatantId);
            round.TickBlocked.Add(new BlockedAction(combatantId, action, stun.Id));
        }
    }

    // ----- Ability activations (start of each tick) -----

    /// <summary>
    /// Decides which abilities queued at this tick go ahead (3B.3). An ability is used once its tick
    /// is reached by a caster who is still standing, whatever then happens to its targets, and that
    /// starts its cooldown (ABILITIES.md A10). One that isn't in the loadout or is on cooldown is
    /// refused (planning rejects both; this only guards the engine). The ones that go ahead are
    /// recorded in <see cref="RoundContext.TickAbilities"/> for this tick's phases, and every
    /// activation in <see cref="RoundContext.TickUses"/>.
    /// </summary>
    private static void ResolveAbilityActivations(RoundContext round, int tick)
    {
        round.TickAbilities.Clear();
        round.TickUses.Clear();
        round.TickConditions.Clear();
        round.TickTargets.Clear();
        round.TickHits.Clear();
        round.TickWatching.Clear();
        var uses = round.TickUses;
        foreach (var (casterId, queue) in round.Plans)
        {
            if (!round.Acts(casterId)) continue;
            var action = ActionAt(queue, tick);
            if (action?.Type != ActionType.Ability) continue;

            var caster = round.Combatants[casterId];
            var ability = caster.Profile.FindAbility(action.AbilityId);
            var targets = TargetIdsOf(action);
            var abilityId = action.AbilityId ?? "";

            bool prepared = ability is { IsPrepared: true, Trigger: not null };
            if (ability is null || (ability.Activation != AbilityActivation.Active && !prepared))
            {
                uses.Add(new AbilityUse(casterId, abilityId, targets, WastedReason.NotInLoadout));
                continue;
            }

            var used = round.UsedAbilities.TryGetValue(casterId, out var set) ? set : round.UsedAbilities[casterId] = [];
            bool usedEarlierThisRound = ability.Cooldown > 0 && used.Contains(ability.Id);
            if (caster.CooldownLeft(ability.Id) > 0 || usedEarlierThisRound)
            {
                uses.Add(new AbilityUse(casterId, abilityId, targets, WastedReason.OnCooldown));
                continue;
            }

            used.Add(ability.Id);
            if (prepared)
            {
                // A prepared reaction (SPRINT_3B6 X8): it starts watching now, before this tick's movement, and its
                // cooldown starts whether or not it fires. Its effects resolve only when its trigger is met.
                round.Watching[casterId] = ability;
                round.TickWatching.Add(new PreparedReaction(casterId, ability.Id));
                continue;
            }
            round.TickAbilities[casterId] = new ActiveAbility(ability, action, uses.Count);
            uses.Add(new AbilityUse(casterId, abilityId, targets, StartedCooldown: ability.Cooldown > 0));
        }
    }

    /// <summary>The chosen targets of an action: the single target, or the list.</summary>
    private static IReadOnlyList<string> TargetIdsOf(PlannedAction action) =>
        action.TargetCombatantIds is { Count: > 0 } list ? list
        : string.IsNullOrEmpty(action.TargetCombatantId) ? []
        : [action.TargetCombatantId];

    /// <summary>
    /// Who an ability affects this tick, in the order its rolls are made: the chosen targets in plan
    /// order, the caster for <see cref="TargetKind.Self"/>, or for <see cref="TargetKind.Auto"/> the
    /// ones found now (after this tick's movement). An auto ability that finds nobody is marked
    /// <see cref="WastedReason.NoTargets"/> on its use; its cooldown still starts.
    /// </summary>
    private static IReadOnlyList<string> ResolveTargets(RoundContext round, string casterId, ActiveAbility active)
    {
        switch (active.Ability.Targeting.Kind)
        {
            case TargetKind.Self:
                return [casterId];
            case TargetKind.Auto:
                var found = FindAutoTargets(round, casterId, active.Ability.Targeting);
                round.TickUses[active.UseIndex] = round.TickUses[active.UseIndex] with
                {
                    TargetIds = found,
                    WastedReason = found.Count == 0 ? WastedReason.NoTargets : WastedReason.None,
                };
                return found;
            default:
                return TargetIdsOf(active.Action);
        }
    }

    /// <summary>
    /// Targets of an <see cref="TargetKind.Auto"/> ability (ABILITIES.md A4): everyone who passes the
    /// filter and is within range of the caster right now, nearest first, ties in the state's combatant
    /// order (the Api loads combatants in creation order, so this repeats from run to run), at
    /// most <see cref="Targeting.Count"/>. Only standing combatants count, except for a
    /// <see cref="TargetFilter.DownedAlly"/> filter, which looks for Downed ones; nobody is finished
    /// off or revived by accident.
    /// </summary>
    private static List<string> FindAutoTargets(RoundContext round, string casterId, Targeting targeting)
    {
        if (!round.Positions.TryGetValue(casterId, out var from)) return [];
        var caster = round.Combatants[casterId];
        var wanted = targeting.Filter == TargetFilter.DownedAlly ? CombatantStatus.Downed : CombatantStatus.Standing;
        var found = round.State.Combatants
            .Where(c => round.Status[c.Id] == wanted
                        && Relations.Matches(caster, c, targeting)
                        && round.WithinRange(casterId, c.Id, targeting.Range))
            .OrderBy(c => from.ChebyshevDistanceTo(round.Positions[c.Id])) // stable: ties keep the state's order
            .Select(c => c.Id);
        return (targeting.Count is { } n ? found.Take(n) : found).ToList();
    }

    // ----- Defend (start of each tick) -----

    /// <summary>
    /// Defend grants Guarded at the start of its own tick, before Movement (SPRINT_3B4 C7), so it counts
    /// against this tick's attacks and every later one this round. It lasts until the end of the round;
    /// defending again refreshes it.
    /// </summary>
    private static void ResolveGuards(RoundContext round, int tick)
    {
        foreach (var (combatantId, queue) in round.Plans)
        {
            if (!round.Acts(combatantId) || ActionAt(queue, tick)?.Type != ActionType.Defend) continue;
            round.TickConditions.Add(round.ApplyCondition(
                combatantId, combatantId, round.Config.GuardedCondition, duration: 0, abilityId: null));
        }
    }

    // ----- Phase 1: Movement -----

    private static List<MovementOutcome> ResolveMovementPhase(RoundContext round, int tick)
    {
        round.AbilityCellsMoved.Clear();
        round.TickReactions.Clear();
        round.PassiveUsedThisTick.Clear();
        // Where everyone stands as the phase starts: a move to a target heads for this position.
        var startPositions = new Dictionary<string, GridPosition>(round.Positions);
        var movers = new List<Mover>();
        foreach (var (combatantId, queue) in round.Plans)
        {
            if (!round.Acts(combatantId)) continue;
            var action = ActionAt(queue, tick);
            if (action?.Type == ActionType.Ability)
            {
                if (round.TickAbilities.TryGetValue(combatantId, out var active)
                    && AbilityMover(round, combatantId, active, startPositions) is { } abilityMover)
                    movers.Add(abilityMover);
                continue;
            }
            if (action?.Type is not (ActionType.Move or ActionType.Disengage) || action.TargetCell is not { } target) continue;

            // Rooted (C8): the move doesn't happen, and is reported as blocked.
            if (ConditionMath.BlockingCondition(round.Conditions[combatantId], BlockKind.Move) is { } root)
            {
                round.TickBlocked.Add(new BlockedAction(combatantId, action, root.Id, BlockKind.Move));
                continue;
            }

            // A combatant with no map position isn't on the map, so it can't move.
            if (!round.Positions.TryGetValue(combatantId, out var start)) continue;

            // Re-check the move against the rules (PlanningService also validates it): the path
            // stops when the mover's movement budget for the round runs out (all moves share
            // MovementRange cells, roadmap 3B.2, changed by conditions at the start of the round, C6)
            // or at the grid edge, whichever comes first.
            var mover = round.Combatants[combatantId];
            int cellsLeft = round.MovementRange[combatantId] - round.CellsMoved.GetValueOrDefault(combatantId);
            var path = LegalPrefix(ComputeChebyshevPath(start, target), round.State, Math.Max(0, cellsLeft));

            // A move to the combatant's own cell is a mover with a one-cell path: it never steps,
            // blocks like any stationary occupant, and still reports a (stay-put) result.
            movers.Add(new Mover(combatantId, path, mover.Speed) { Disengaging = action.Type == ActionType.Disengage });
        }

        if (movers.Count == 0) return [];
        // A combatant whose own action this tick moves it can't make opportunity attacks this tick (SPRINT_3B6 X5).
        var moving = movers.Select(m => m.CombatantId).ToHashSet();

        var byId = movers.ToDictionary(m => m.CombatantId);
        int maxSteps = movers.Max(m => m.FullPath.Count - 1);

        // Lockstep: every mover takes step 1, then step 2, ... (TICK_RESOLUTION Decision 6).
        for (int step = 0; step < maxSteps; step++)
        {
            var active = movers.Where(m => !m.Stopped && m.CurrentStep < m.FullPath.Count - 1).ToList();
            if (active.Count == 0) break;

            var desired = active.ToDictionary(m => m.CombatantId, m => m.FullPath[m.CurrentStep + 1]);
            var activeIds = active.Select(m => m.CombatantId).ToHashSet();
            var blocked = new HashSet<string>();

            // Mover-vs-mover: several movers claim the same cell this step.
            foreach (var group in desired.GroupBy(kv => kv.Value).Where(g => g.Count() > 1))
            {
                var claimants = group.Select(kv => kv.Key).ToList();
                var bySpeed = claimants.OrderByDescending(id => byId[id].Speed).ToList();
                int highest = byId[bySpeed[0]].Speed;
                int second = byId[bySpeed[1]].Speed;

                if (highest == second)
                {
                    // Tied speed (top two only) — all claimants stop.
                    foreach (var id in claimants)
                    {
                        blocked.Add(id);
                        byId[id].BlockedBy = claimants.First(other => other != id);
                    }
                }
                else
                {
                    // Higher Speed wins (DESIGN_SPIKE / TICK_RESOLUTION): the fastest claimant
                    // takes the cell regardless of plan order; everyone else is blocked by it.
                    foreach (var id in bySpeed.Skip(1))
                    {
                        blocked.Add(id);
                        byId[id].BlockedBy = bySpeed[0];
                    }
                }
            }

            // Mover-vs-occupant: a cell is blocked by anyone standing in it (including
            // defeated combatants — quirk 9) unless that occupant is an active mover that
            // isn't blocked, so movers can swap or pass through each other (quirk 2).
            // `blocked` grows during this loop, so later movers see earlier blocks.
            foreach (var (moverId, nextCell) in desired)
            {
                if (blocked.Contains(moverId)) continue;

                foreach (var (occupantId, pos) in round.Positions)
                {
                    if (occupantId == moverId || pos != nextCell) continue;

                    bool isActiveUnblockedMover = activeIds.Contains(occupantId) && !blocked.Contains(occupantId);
                    if (!isActiveUnblockedMover)
                    {
                        blocked.Add(moverId);
                        byId[moverId].BlockedBy = occupantId;
                        break;
                    }
                }
            }

            var stepped = new List<(Mover Mover, GridPosition From, GridPosition To)>();
            foreach (var mover in active)
            {
                if (blocked.Contains(mover.CombatantId))
                {
                    mover.Stopped = true;
                }
                else
                {
                    stepped.Add((mover, mover.FullPath[mover.CurrentStep], mover.FullPath[mover.CurrentStep + 1]));
                    mover.CurrentStep++;
                    round.Positions[mover.CombatantId] = mover.FullPath[mover.CurrentStep];
                    // Only steps actually taken use up the movement budget; an ability's own cells
                    // (Dash) don't.
                    if (mover.UsesBudget)
                        round.CellsMoved[mover.CombatantId] = round.CellsMoved.GetValueOrDefault(mover.CombatantId) + 1;
                }
            }

            // The reaction hook (SPRINT_3B6 X1, X3): everything this step provoked resolves now, as one batch.
            ResolveStepReactions(round, step + 1, stepped, moving);
        }

        foreach (var m in movers.Where(m => m.AbilityId is not null))
            round.AbilityCellsMoved[m.CombatantId] = m.CurrentStep;

        return movers.Select(m => new MovementOutcome(
            m.CombatantId,
            m.FullPath.Take(m.CurrentStep + 1).ToList(),
            m.FullPath[m.CurrentStep],
            m.Stopped && !m.Fell,
            m.Fell ? null : m.BlockedBy,
            m.AbilityId,
            m.Fell ? StopReason.Fell : m.Stopped ? StopReason.Blocked : StopReason.None,
            m.Disengaging)).ToList();
    }

    // ----- Reactions at a movement step (3B.6) -----

    /// <summary>
    /// Everything one lockstep step provoked (SPRINT_3B6 X1–X3, X12), resolved as one batch:
    /// <list type="bullet">
    /// <item>Passive opportunity attacks (X2, X5): a mover that isn't disengaging and steps from a cell within an enemy's
    /// melee reach to one outside it gives that enemy a free melee weapon hit, if the enemy is standing, not stopped from
    /// acting (Stunned), makes opportunity attacks, isn't moving this tick itself and hasn't already reacted this tick.</item>
    /// <item>Prepared movement triggers (X12): a watcher whose trigger the step meets (an enemy entering its reach, or
    /// moving within its range; disengaging doesn't help) resolves its ability's attacks on the mover and stops watching.</item>
    /// </list>
    /// Movers in plan order, then reactors in combatant order (a reactor's opportunity attack before its prepared one);
    /// each attack rolls d20 then damage. The step's reactions are one netted batch, then the 0-HP check: a mover
    /// dropped here stops on the cell it reached.
    /// </summary>
    private static void ResolveStepReactions(
        RoundContext round, int step, List<(Mover Mover, GridPosition From, GridPosition To)> stepped, HashSet<string> moving)
    {
        if (stepped.Count == 0) return;
        int reach = round.Config.MeleeReach;
        var fired = new List<(string ReactorId, string TriggerId, ReactionKind Kind, AbilityDefinition? Ability, PendingAttack? Attack)>();
        var hpDeltas = new Dictionary<string, int>();
        foreach (var (mover, from, to) in stepped)
        {
            var moverState = round.Combatants[mover.CombatantId];
            foreach (var reactor in round.State.Combatants)
            {
                if (reactor.Id == mover.CombatantId
                    || round.IsOut(reactor.Id)
                    || Relations.Between(reactor, moverState) != Relation.Enemy
                    || !round.Positions.TryGetValue(reactor.Id, out var at))
                    continue;

                if (!mover.Disengaging
                    && reactor.Profile.OpportunityAttacks
                    && !moving.Contains(reactor.Id)
                    && !round.PassiveUsedThisTick.Contains(reactor.Id)
                    && !ConditionMath.IsBlocked(round.Conditions[reactor.Id], BlockKind.All)
                    && at.ChebyshevDistanceTo(from) <= reach
                    && at.ChebyshevDistanceTo(to) > reach)
                {
                    round.PassiveUsedThisTick.Add(reactor.Id);
                    fired.Add((reactor.Id, mover.CombatantId, ReactionKind.Opportunity, null,
                        RollAttack(round, hpDeltas, reactor.Id, mover.CombatantId, ActionType.MeleeAttack,
                            AttackKind.Melee, DamageSpec.Weapon, accuracyBonus: 0, WastedReason.None, ability: null,
                            tags: OpportunityTags)));
                }

                if (round.Watching.TryGetValue(reactor.Id, out var watch)
                    && MovementTriggerMet(watch.Trigger!, at, from, to, reach))
                {
                    round.Watching.Remove(reactor.Id); // fires once (X9)
                    var attacks = watch.Effects.OfType<AttackEffect>().ToList();
                    if (attacks.Count == 0)
                        fired.Add((reactor.Id, mover.CombatantId, ReactionKind.Prepared, watch, null));
                    foreach (var attack in attacks)
                        fired.Add((reactor.Id, mover.CombatantId, ReactionKind.Prepared, watch,
                            RollAttack(round, hpDeltas, reactor.Id, mover.CombatantId, AttackActionType(attack.Roll),
                                attack.Roll, attack.Damage, attack.AccuracyBonus, WastedReason.None, watch)));
                }
            }
        }
        if (fired.Count == 0) return;

        var hpBefore = new Dictionary<string, int>(round.Hp);
        foreach (var (targetId, delta) in hpDeltas)
            round.Hp[targetId] += delta;
        var fell = new HashSet<string>();
        ResolveFalls(round, fell);

        foreach (var (mover, _, _) in stepped)
            if (fell.Contains(mover.CombatantId))
                mover.Stopped = mover.Fell = true;

        foreach (var f in fired)
            round.TickReactions.Add(new ReactionOutcome(f.ReactorId, f.TriggerId, f.Kind, f.Ability?.Id, step,
                f.Attack is null ? null : ToOutcome(round, f.Attack, hpBefore, fell)));
    }

    /// <summary>
    /// Whether a movement step from <paramref name="from"/> to <paramref name="to"/> meets a prepared movement trigger of a
    /// watcher standing at <paramref name="at"/> (SPRINT_3B6 X12). Attack triggers never fire here.
    /// </summary>
    private static bool MovementTriggerMet(ReactionTrigger trigger, GridPosition at, GridPosition from, GridPosition to, int reach) =>
        trigger.Kind switch
        {
            TriggerKind.EnemyEntersReach => at.ChebyshevDistanceTo(from) > reach && at.ChebyshevDistanceTo(to) <= reach,
            TriggerKind.EnemyMovesWithin => at.ChebyshevDistanceTo(to) <= (trigger.Range ?? reach),
            _ => false,
        };

    /// <summary>The action type an attack effect is reported as.</summary>
    private static ActionType AttackActionType(AttackKind roll) => roll switch
    {
        AttackKind.Melee => ActionType.MeleeAttack,
        AttackKind.Ranged => ActionType.RangedAttack,
        _ => ActionType.Ability,
    };

    /// <summary>Source tags of an opportunity attack, for damage-taken modifiers (SPRINT_3B6 X5).</summary>
    private static readonly string[] OpportunityTags = ["melee", "opportunity"];

    private static AttackOutcome ToOutcome(RoundContext round, PendingAttack a, Dictionary<string, int> hpBefore, HashSet<string> fell) =>
        new(a.AttackerId,
            a.TargetId,
            a.Type,
            a.Hit,
            a.Damage,
            a.IsCrit,
            TargetHpBefore: hpBefore.GetValueOrDefault(a.TargetId, 0),
            TargetHpAfter: round.Hp.GetValueOrDefault(a.TargetId, 0),
            TargetDefeated: fell.Contains(a.TargetId),
            a.Wasted,
            a.Roll,
            TargetStatus: round.Status.GetValueOrDefault(a.TargetId, CombatantStatus.Standing),
            FinishingBlow: a.Finishing,
            AbilityId: a.AbilityId,
            MoveBonus: a.MoveBonus,
            ConditionDamage: a.ConditionDamage,
            InterceptedFromId: a.InterceptedFromId);

    /// <summary>
    /// The mover for an ability's move effect (ABILITIES.md A5, A7), or null if it has none or the
    /// caster isn't on the map. A move to a cell walks toward the planned cell; a move to a target
    /// walks to the free cell in melee reach of where the target stood as the phase began, closest
    /// to the caster (staying put if already in reach, or if the target is unknown, off the map,
    /// beyond the ability's range or has no free cell next to it). Either way the walk is capped at
    /// the effect's <see cref="MoveEffect.MaxCells"/>, the budget left if it
    /// <see cref="MoveEffect.UsesMovementBudget"/>, and the grid edge.
    /// </summary>
    private static Mover? AbilityMover(
        RoundContext round, string casterId, ActiveAbility active, IReadOnlyDictionary<string, GridPosition> startPositions)
    {
        if (active.Ability.Move is not { } move) return null;
        if (!round.Positions.TryGetValue(casterId, out var start)) return null;
        // Rooted (C8): the ability's move doesn't happen; its other effects resolve from where the caster stands.
        if (ConditionMath.IsBlocked(round.Conditions[casterId], BlockKind.Move)) return null;

        var destination = start;
        if (move.To == MoveTo.Cell)
        {
            if (active.Action.TargetCell is { } cell) destination = cell;
        }
        else if (active.Action.TargetCombatantId is { } targetId
                 && startPositions.TryGetValue(targetId, out var targetAt)
                 && start.ChebyshevDistanceTo(targetAt) <= active.Ability.Targeting.Range)
        {
            destination = DestinationNear(start, targetAt, round.Config.MeleeReach, round.State.GridWidth, round.State.GridHeight,
                c => !startPositions.Values.Contains(c)) ?? start;
        }

        var caster = round.Combatants[casterId];
        int cells = move.MaxCells;
        if (move.UsesMovementBudget)
            cells = Math.Min(cells, round.MovementRange[casterId] - round.CellsMoved.GetValueOrDefault(casterId));
        var path = LegalPrefix(ComputeChebyshevPath(start, destination), round.State, Math.Max(0, cells));
        return new Mover(casterId, path, caster.Speed)
        {
            AbilityId = active.Ability.Id, UsesBudget = move.UsesMovementBudget, Disengaging = move.Disengage,
        };
    }

    /// <summary>
    /// Where a move to a target ends (ABILITIES.md A5): <paramref name="from"/> if it is already within
    /// <paramref name="reach"/> of <paramref name="target"/>; otherwise the free on-grid cell within reach
    /// of the target that is closest to <paramref name="from"/>, ties to the cell most in line with the
    /// target (smallest |dx| + |dy|), then top-to-bottom, left-to-right. Null when no such cell is free.
    /// Shared by the resolver, <see cref="PlanValidator"/> (estimates) and <see cref="EnemyAi"/>.
    /// </summary>
    public static GridPosition? DestinationNear(
        GridPosition from, GridPosition target, int reach, int gridWidth, int gridHeight, Func<GridPosition, bool> isFree)
    {
        if (from.ChebyshevDistanceTo(target) <= reach) return from;
        GridPosition? best = null;
        (int, int, int, int) bestKey = default;
        for (int y = target.Y - reach; y <= target.Y + reach; y++)
            for (int x = target.X - reach; x <= target.X + reach; x++)
            {
                var cell = new GridPosition(x, y);
                if (cell == target || x < 0 || y < 0 || x >= gridWidth || y >= gridHeight || !isFree(cell)) continue;
                var key = (from.ChebyshevDistanceTo(cell), Math.Abs(x - target.X) + Math.Abs(y - target.Y), y, x);
                if (best is null || key.CompareTo(bestKey) < 0) (best, bestKey) = (cell, key);
            }
        return best;
    }

    // ----- Phase 2: Attacks -----

    private sealed record PendingAttack(
        string AttackerId, string TargetId, ActionType Type, WastedReason Wasted, bool Hit, int Damage, bool IsCrit,
        AttackRoll? Roll, bool Finishing, string? AbilityId, int MoveBonus, int ConditionDamage)
    {
        /// <summary>An Intercept made this attack on <see cref="TargetId"/> instead of this ally (3B.6 PR 3).</summary>
        public string? InterceptedFromId { get; init; }
    }

    private sealed record PendingHeal(
        string HealerId, string TargetId, string AbilityId, WastedReason Reason, int Amount, bool CanRevive)
    {
        public bool Wasted => Reason != WastedReason.None;
    }

    /// <summary>
    /// One attack of the tick's Attack phase, collected (with its reach check) before anything is rolled, so attack
    /// triggers can change it first (3B.6 PR 3, SPRINT_3B6 X13).
    /// </summary>
    /// <param name="SingleTarget">A basic attack or a <see cref="TargetKind.Combatant"/> ability: Intercept can take it.</param>
    private sealed record AttackIntent(
        string AttackerId, string TargetId, ActionType Type, AttackKind Kind, DamageSpec Damage, int AccuracyBonus,
        WastedReason Wasted, AbilityDefinition? Ability, int CellsMoved, bool SingleTarget)
    {
        public string? InterceptedFromId { get; init; }
        public int ReactionDodge { get; init; }
    }

    /// <summary>One heal of the tick's Attack phase, collected before anything is rolled (rolls keep plan order).</summary>
    private sealed record HealIntent(string HealerId, string TargetId, HealEffect Heal, WastedReason Wasted, string AbilityId);

    /// <summary>
    /// Attacks and heals (ABILITIES.md A8) are one batch: every attack and heal is collected first, with its reach
    /// check, in plan order (then effect order, then target order within an ability); attack triggers run on that list
    /// (3B.6 PR 3: Intercept, then Parry); then dice are rolled in the same order, and all damage and healing is netted
    /// together before anyone falls or gets back up.
    /// </summary>
    private static (List<AttackOutcome>, List<HealOutcome>) ResolveAttackPhase(RoundContext round, int tick)
    {
        var items = new List<object>(); // AttackIntent | HealIntent, in roll order
        round.TickRedirects.Clear();

        foreach (var (attackerId, queue) in round.Plans)
        {
            if (!round.Acts(attackerId)) continue;
            var action = ActionAt(queue, tick);
            switch (action?.Type)
            {
                case ActionType.MeleeAttack:
                case ActionType.RangedAttack:
                {
                    bool melee = action.Type == ActionType.MeleeAttack;
                    // Ranged attacks have unlimited range in 3A; melee needs reach after movement.
                    var wasted = CheckAttackTarget(round, attackerId, action.TargetCombatantId ?? "",
                        targeting: null, range: melee ? round.Config.MeleeReach : null, WastedReason.OutOfMeleeReach);
                    items.Add(new AttackIntent(attackerId, action.TargetCombatantId ?? "", action.Type,
                        melee ? AttackKind.Melee : AttackKind.Ranged, DamageSpec.Weapon, 0, wasted, null, 0, SingleTarget: true));
                    break;
                }
                case ActionType.Ability when round.TickAbilities.TryGetValue(attackerId, out var active):
                {
                    var ability = active.Ability;
                    var targets = ResolveTargets(round, attackerId, active);
                    round.TickTargets[attackerId] = targets; // the Effects phase applies conditions to the same targets
                    foreach (var effect in ability.Effects)
                    {
                        switch (effect)
                        {
                            case AttackEffect attack:
                                var type = AttackActionType(attack.Roll);
                                // An ability that moves checks its range when the move starts; its melee
                                // attack then needs melee reach from where the move ended (Charge).
                                int reach = ability.Move is not null && attack.Roll == AttackKind.Melee
                                    ? round.Config.MeleeReach
                                    : ability.Targeting.Range;
                                int moved = round.AbilityCellsMoved.GetValueOrDefault(attackerId);
                                foreach (var targetId in targets)
                                {
                                    var wasted = CheckAttackTarget(round, attackerId, targetId,
                                        ability.Targeting, reach, WastedReason.OutOfRange);
                                    items.Add(new AttackIntent(attackerId, targetId, type, attack.Roll, attack.Damage,
                                        attack.AccuracyBonus, wasted, ability, moved,
                                        SingleTarget: ability.Targeting.Kind == TargetKind.Combatant));
                                }
                                break;
                            case HealEffect heal:
                                foreach (var targetId in targets)
                                    items.Add(new HealIntent(attackerId, targetId, heal,
                                        CheckHealTarget(round, attackerId, targetId, ability.Targeting, heal), ability.Id));
                                break;
                        }
                    }
                    break;
                }
            }
        }

        ResolveAttackTriggers(round, items);

        var pending = new List<PendingAttack>();
        var heals = new List<PendingHeal>();
        var hpDeltas = new Dictionary<string, int>(); // targetId → net delta (negative = damage)
        foreach (var item in items)
        {
            if (item is HealIntent h)
            {
                heals.Add(RollHeal(round, hpDeltas, h.HealerId, h.TargetId, h.Heal, h.Wasted, h.AbilityId));
                continue;
            }
            var a = (AttackIntent)item;
            var rolled = RollAttack(round, hpDeltas, a.AttackerId, a.TargetId, a.Type, a.Kind, a.Damage, a.AccuracyBonus,
                a.Wasted, a.Ability, a.CellsMoved, reactionDodge: a.ReactionDodge) with { InterceptedFromId = a.InterceptedFromId };
            pending.Add(rolled);
            if (a.Ability is not null && rolled.Hit)
                (round.TickHits.TryGetValue(a.AttackerId, out var hits) ? hits : round.TickHits[a.AttackerId] = []).Add(a.TargetId);
        }

        // Snapshot, then apply all deltas as one netted batch (DESIGN_SPIKE Decision 3). Anyone
        // healed is capped at MaxHp after netting: 18/20 taking 5 and healing 8 ends on 20.
        var hpBefore = new Dictionary<string, int>(round.Hp);
        foreach (var (targetId, delta) in hpDeltas)
            round.Hp[targetId] += delta;
        foreach (var h in heals.Where(h => !h.Wasted))
            round.Hp[h.TargetId] = Math.Min(round.Hp[h.TargetId], round.Combatants[h.TargetId].MaxHp);

        // Finishing blows land together with the damage: a Downed or Stable target that was hit dies,
        // even if a heal reached it in the same tick (the blow wins, ABILITIES.md A8).
        var fellThisTick = new HashSet<string>();
        foreach (var a in pending.Where(a => a.Finishing && a.Hit))
        {
            round.Hp[a.TargetId] = 0;
            if (round.Status[a.TargetId] == CombatantStatus.Dead) continue;
            round.SetStatus(a.TargetId, CombatantStatus.Dead);
            fellThisTick.Add(a.TargetId);
        }

        // Revives: a healed target that was Downed or Stable before this tick and still is stands
        // back up with the healed HP, and stops bleeding out. Their queue stays void this round.
        var revivedThisTick = new HashSet<string>();
        foreach (var h in heals.Where(h => !h.Wasted && h.CanRevive))
        {
            if (round.Status[h.TargetId] is not (CombatantStatus.Downed or CombatantStatus.Stable)) continue;
            if (round.Hp[h.TargetId] <= 0) continue;
            round.SetStatus(h.TargetId, CombatantStatus.Standing);
            round.Bleedout[h.TargetId] = 0;
            // If they fall again this round, that's a new fall: no finishing blow, no bleedout tick yet.
            round.OutAtStart.Remove(h.TargetId);
            round.DownedAtStart.Remove(h.TargetId);
            revivedThisTick.Add(h.TargetId);
        }

        // The 0-HP check runs once, after ALL damage is applied — enables mutual kills
        // (DESIGN_SPIKE Decision 2).
        ResolveFalls(round, fellThisTick);

        var attackOutcomes = pending.Select(a => ToOutcome(round, a, hpBefore, fellThisTick)).ToList();

        var healOutcomes = heals.Select(h => new HealOutcome(
            h.HealerId,
            h.TargetId,
            h.AbilityId,
            h.Amount,
            TargetHpBefore: hpBefore.GetValueOrDefault(h.TargetId, 0),
            TargetHpAfter: round.Hp.GetValueOrDefault(h.TargetId, 0),
            Revived: revivedThisTick.Contains(h.TargetId) && !h.Wasted,
            h.Reason,
            TargetStatus: round.Status.GetValueOrDefault(h.TargetId, CombatantStatus.Standing))).ToList();

        return (attackOutcomes, healOutcomes);
    }

    /// <summary>
    /// Attack triggers (3B.6 PR 3, SPRINT_3B6 X13), after the tick's movement and before any Attack-phase roll, on the
    /// collected attacks (a wasted one, e.g. out of reach, triggers nothing). Watchers in combatant order.
    /// <list type="number">
    /// <item><see cref="TriggerKind.AllyAttacked"/> with a <see cref="RedirectEffect"/> (Intercept): the first
    /// single-target attack in plan order by an enemy on an ally within the trigger's range is made on the watcher instead
    /// (reach waived). A redirected attack is no longer aimed at an ally, so no second Intercept takes it. An ability
    /// attack's targets for the Effects phase follow it.</item>
    /// <item><see cref="TriggerKind.Attacked"/> with a <see cref="ModifyIncomingEffect"/> (Parry), on the final targets:
    /// the watcher gets the extra Dodge against every matching attack on it this tick.</item>
    /// </list>
    /// A watcher that fires stops watching (X9) and is reported with step 0 and no attack.
    /// </summary>
    private static void ResolveAttackTriggers(RoundContext round, List<object> items)
    {
        if (round.Watching.Count == 0) return;
        var watchers = round.State.Combatants.Where(c => round.Watching.ContainsKey(c.Id)).ToList();

        foreach (var watcher in watchers)
        {
            var watch = round.Watching[watcher.Id];
            if (watch.Trigger is not { Kind: TriggerKind.AllyAttacked } trigger || !watch.Effects.OfType<RedirectEffect>().Any())
                continue;
            int range = trigger.Range ?? round.Config.MeleeReach;
            int index = items.FindIndex(i => i is AttackIntent
            {
                Wasted: WastedReason.None, SingleTarget: true, InterceptedFromId: null,
            } a
                && a.TargetId != watcher.Id
                && Relations.Between(watcher, round.Combatants[a.TargetId]) == Relation.Ally
                && Relations.Between(watcher, round.Combatants[a.AttackerId]) == Relation.Enemy
                && round.WithinRange(watcher.Id, a.TargetId, range));
            if (index < 0) continue;

            var attack = (AttackIntent)items[index];
            items[index] = attack with { TargetId = watcher.Id, InterceptedFromId = attack.TargetId };
            if (attack.Ability is not null)
            {
                // The ability's other effects (a condition on a hit) follow the attack to the interceptor.
                round.TickTargets[attack.AttackerId] = [watcher.Id];
                round.TickRedirects[attack.AttackerId] = watcher.Id;
            }
            round.Watching.Remove(watcher.Id);
            round.TickReactions.Add(new ReactionOutcome(watcher.Id, attack.AttackerId, ReactionKind.Prepared, watch.Id, 0, null)
            {
                InterceptedFromId = attack.TargetId,
            });
        }

        foreach (var watcher in watchers)
        {
            if (!round.Watching.TryGetValue(watcher.Id, out var watch)
                || watch.Trigger is not { Kind: TriggerKind.Attacked } trigger)
                continue;
            int dodge = watch.Effects.OfType<ModifyIncomingEffect>().Sum(e => e.Dodge);
            var matching = items
                .Select((item, i) => (Item: item as AttackIntent, Index: i))
                .Where(x => x.Item is { Wasted: WastedReason.None } a && a.TargetId == watcher.Id
                            && (trigger.AttackKind is null || a.Kind == trigger.AttackKind))
                .ToList();
            if (matching.Count == 0) continue;

            foreach (var (a, i) in matching)
                items[i] = a! with { ReactionDodge = a.ReactionDodge + dodge };
            round.Watching.Remove(watcher.Id);
            round.TickReactions.Add(new ReactionOutcome(watcher.Id, matching[0].Item!.AttackerId, ReactionKind.Prepared, watch.Id, 0, null)
            {
                ReactionDodge = dodge,
            });
        }
    }

    /// <summary>
    /// The 0-HP check, shared by every tick's attacks and the end of round's periodic effects. It
    /// deliberately covers every combatant, not just the ones just hit: anyone standing at HP ≤ 0 falls
    /// however they got there. Those who can be downed go Downed and start bleeding out (their countdown
    /// starts next round); everyone else dies (3B.5). HP stops at 0. Falling clears conditions (C10).
    /// </summary>
    /// <param name="fell">Collects the ids of those who fell.</param>
    private static void ResolveFalls(RoundContext round, HashSet<string> fell)
    {
        foreach (var c in round.State.Combatants)
        {
            if (round.IsOut(c.Id) || round.Hp[c.Id] > 0) continue;
            round.Hp[c.Id] = 0;
            if (c.Profile.CanBeDowned)
            {
                round.SetStatus(c.Id, CombatantStatus.Downed);
                round.Bleedout[c.Id] = round.Config.BleedoutRounds;
            }
            else
            {
                round.SetStatus(c.Id, CombatantStatus.Dead);
            }
            fell.Add(c.Id);
        }
    }

    /// <summary>
    /// Whether a heal can reach a target right now (the heal side of <see cref="CheckAttackTarget"/>):
    /// the target exists → it's on the side the targeting allows → it isn't dead, and if it's down
    /// the heal can revive (or, for a downed-ally heal, it is Downed) → it's within range of the
    /// healer's current cell. Unlike attacks, a heal reaches someone who went down earlier this round.
    /// </summary>
    private static WastedReason CheckHealTarget(
        RoundContext round, string healerId, string targetId, Targeting targeting, HealEffect heal)
    {
        if (!round.Combatants.TryGetValue(targetId, out var target))
            return WastedReason.InvalidTarget;
        if (!Relations.Matches(round.Combatants[healerId], target, targeting))
            return WastedReason.InvalidTargetType;

        var status = round.Status[targetId];
        if (status == CombatantStatus.Dead || (status != CombatantStatus.Standing && !heal.CanRevive))
            return WastedReason.TargetDefeated;
        if (targeting.Filter == TargetFilter.DownedAlly && status != CombatantStatus.Downed)
            return WastedReason.TargetNotDowned;

        if (healerId != targetId && !round.WithinRange(healerId, targetId, targeting.Range))
            return WastedReason.OutOfRange;
        return WastedReason.None;
    }

    /// <summary>Rolls one heal (unless wasted) and adds it to the tick's netting batch as a positive delta.</summary>
    private static PendingHeal RollHeal(
        RoundContext round, Dictionary<string, int> hpDeltas, string healerId, string targetId, HealEffect heal,
        WastedReason wasted, string abilityId)
    {
        int amount = 0;
        if (wasted == WastedReason.None)
        {
            int rolled = heal.Die is { } die ? round.Dice.Roll(die.Sides) : 0;
            int resonanceBonus = 0;                          // resonance (3M.5)
            int leyBonus = 0;                                // ley points (3M.5)
            amount = Math.Max(0, rolled + heal.Bonus + resonanceBonus + leyBonus);
            hpDeltas[targetId] = hpDeltas.GetValueOrDefault(targetId, 0) + amount;
        }
        return new PendingHeal(healerId, targetId, abilityId, wasted, amount, heal.CanRevive);
    }

    /// <summary>
    /// The single place that decides whether an attacker can hit a target right now (ABILITIES.md
    /// A4): the target exists → it's on the side the targeting allows → it isn't already out of
    /// reach of attacks by status → it's within range of the attacker's current cell. Line of
    /// sight (3M.2), the high-ground range bonus (3M.4) and "the attacker can see the target"
    /// (3C.1) belong here too. Nothing else in the resolver checks range.
    /// </summary>
    /// <param name="targeting">The ability's targeting, for its side filter; null for basic attacks (anyone).</param>
    /// <param name="range">Max Chebyshev distance, or null for unlimited (basic ranged attacks).</param>
    /// <param name="outOfRange">The reason to report when the target is too far (basic melee keeps its own).</param>
    private static WastedReason CheckAttackTarget(
        RoundContext round, string attackerId, string targetId, Targeting? targeting, int? range, WastedReason outOfRange)
    {
        if (!round.Combatants.TryGetValue(targetId, out var target))
            return WastedReason.InvalidTarget;
        if (targeting is not null && !Relations.Matches(round.Combatants[attackerId], target, targeting))
            return WastedReason.InvalidTargetType;

        // Damage within a tick is simultaneous, so nobody falls mid-phase (quirk 6). A target that
        // fell in an EARLIER tick is wasted as defeated — dead, or downed this round: finishing
        // blows must be planned, so only someone already Downed or Stable when the round began can
        // be finished off (3B.5). That way a queued second attack never kills a hero before allies
        // can react.
        if (round.Status[targetId] == CombatantStatus.Dead
            || (round.IsOut(targetId) && !round.OutAtStart.Contains(targetId)))
            return WastedReason.TargetDefeated;

        // A combatant with no map position isn't on the map: it can't be reached by anything with a range.
        if (range is { } r && !round.WithinRange(attackerId, targetId, r))
            return outOfRange;
        return WastedReason.None;
    }

    /// <summary>Rolls one attack (unless wasted) and adds its damage to the tick's netting batch.</summary>
    /// <param name="cellsMoved">Cells the same activation's move effect covered this tick (for <see cref="DamageSpec.BonusIfMoved"/>).</param>
    /// <param name="ability">The ability making the attack, or null for a basic attack (its tags filter damage-taken modifiers).</param>
    /// <param name="tags">Source tags to use instead of the ability's or the attack kind's (opportunity attacks).</param>
    /// <param name="reactionDodge">Extra Dodge for the target from its prepared reaction (Parry, 3B.6 PR 3).</param>
    private static PendingAttack RollAttack(
        RoundContext round, Dictionary<string, int> hpDeltas, string attackerId, string targetId, ActionType type,
        AttackKind kind, DamageSpec damage, int accuracyBonus, WastedReason wasted, AbilityDefinition? ability, int cellsMoved = 0,
        IReadOnlyCollection<string>? tags = null, int reactionDodge = 0)
    {
        bool hit = false, isCrit = false, finishing = false;
        int amount = 0, moveBonus = 0, conditionDamage = 0;
        AttackRoll? roll = null;
        if (wasted == WastedReason.None)
        {
            // Downed or Stable before this tick's attacks: any hit kills, with no damage roll.
            finishing = round.Status[targetId] != CombatantStatus.Standing;
            // Conditions as they stand now: anything applied in this tick's Effects phase counts from the
            // next tick (SPRINT_3B4 C6). Defend's Guarded, applied at the start of the tick, already counts.
            var mods = new AttackConditions(
                round.Conditions[attackerId], round.Conditions[targetId], tags ?? ConditionMath.AttackTags(ability, kind));
            (hit, amount, isCrit, roll, moveBonus, conditionDamage) = ComputeAttack(
                round.Combatants[attackerId], round.Combatants[targetId], kind, damage, accuracyBonus, round.Dice,
                mods, rollDamage: !finishing, cellsMoved, reactionDodge);
            if (hit && !finishing)
                hpDeltas[targetId] = hpDeltas.GetValueOrDefault(targetId, 0) - amount;
        }
        return new PendingAttack(attackerId, targetId, type, wasted, hit, amount, isCrit, roll, finishing, ability?.Id, moveBonus, conditionDamage);
    }

    /// <summary>The conditions that bear on one attack: the attacker's, the target's, and the attack's tags.</summary>
    private sealed record AttackConditions(
        IReadOnlyList<ActiveCondition> Attacker, IReadOnlyList<ActiveCondition> Target, IReadOnlyCollection<string> SourceTags);

    /// <summary>
    /// Damage pipeline (DESIGN_SPIKE Decision 4, MAGIC_SYSTEM.md). The slots exist now so items,
    /// stats, resonance and crits can fill them in later without restructuring.
    /// </summary>
    /// <returns>Roll is null for an attack that can't miss (<see cref="AttackKind.None"/>). MoveBonus is
    /// the part of the damage from <see cref="DamageSpec.BonusIfMoved"/>, before the multiplier.
    /// ConditionDamage is how much conditions changed the damage, signed (SPRINT_3B4 C4).</returns>
    private static (bool Hit, int Damage, bool IsCrit, AttackRoll? Roll, int MoveBonus, int ConditionDamage) ComputeAttack(
        CombatantState attacker, CombatantState target, AttackKind kind, DamageSpec spec, int accuracyBonus,
        IDiceRoller dice, AttackConditions conditions, bool rollDamage = true, int cellsMoved = 0, int reactionDodge = 0)
    {
        AttackRoll? roll = null;
        if (kind != AttackKind.None)
        {
            int conditionAccuracy = ConditionMath.AccuracyBonus(conditions.Attacker, kind);
            int conditionDodge = ConditionMath.DodgeBonus(conditions.Target, kind);
            int accuracy = (kind == AttackKind.Melee ? attacker.Profile.MeleeAccuracy : attacker.Profile.RangedAccuracy)
                + accuracyBonus + conditionAccuracy;
            roll = new AttackRoll(dice.RollD20(), accuracy, target.Profile.Dodge + conditionDodge + reactionDodge)
            {
                ConditionAccuracy = conditionAccuracy,
                ConditionDodge = conditionDodge,
                ReactionDodge = reactionDodge,
            };
            if (roll.Total < roll.TargetDodge)
                return (false, 0, false, roll, 0, 0);
        }
        if (!rollDamage)
            return (true, 0, false, roll, 0, 0); // a finishing blow: the hit is what counts, conditions don't matter

        int baseDamage = dice.Roll((spec.Die ?? attacker.Profile.BaseDamage).Sides);
        int statModifier = 0;                                // stats (deferred)
        int weaponBonus = 0;                                 // items (4.2)
        int moveBonus = spec.BonusIfMoved is { } ifMoved && cellsMoved >= ifMoved.MinCells ? ifMoved.Bonus : 0;
        int abilityModifier = spec.Bonus + moveBonus;        // abilities (3B.3), incl. Charge's bonus
        int conditionModifier = ConditionMath.DamageDealtBonus(conditions.Attacker); // attacker's conditions (3B.4)
        int resonanceBonus = 0;                              // resonance (3M.5)
        int leyBonus = 0;                                    // ley points (3M.5)
        int totalDamage = (baseDamage + statModifier + weaponBonus + abilityModifier + conditionModifier + resonanceBonus + leyBonus)
            * spec.Multiplier;

        bool isCrit = false;                                 // crit system (3B+)
        if (isCrit) totalDamage *= 2;

        // The target's damage taken (3B.4): flat, then percent, rounded down, never below 0.
        int beforeConditions = totalDamage - conditionModifier * spec.Multiplier;
        totalDamage = ConditionMath.ApplyDamageTaken(Math.Max(0, totalDamage), conditions.Target, conditions.SourceTags);

        return (true, totalDamage, isCrit, roll, moveBonus, totalDamage - beforeConditions);
    }

    // ----- Phase 3: Effects -----

    private static List<EffectOutcome> ResolveEffectsPhase(RoundContext round, int tick)
    {
        var results = new List<EffectOutcome>();
        foreach (var (combatantId, queue) in round.Plans)
        {
            // Includes combatants who fell in this tick's attack phase.
            if (!round.Acts(combatantId)) continue;
            var action = ActionAt(queue, tick);
            switch (action?.Type)
            {
                case ActionType.Defend:
                    // Its effect, Guarded, was applied at the start of the tick (ResolveGuards).
                    results.Add(new EffectOutcome(combatantId, EffectType.Defend));
                    break;
                case ActionType.Stabilize:
                    results.Add(ResolveStabilize(round, combatantId, action.TargetCombatantId ?? ""));
                    break;
                case ActionType.Ability when round.TickAbilities.TryGetValue(combatantId, out var active):
                    ResolveAbilityConditions(round, combatantId, active);
                    break;
            }
        }
        return results;
    }

    /// <summary>
    /// An ability's <see cref="ApplyConditionEffect"/>s (SPRINT_3B4 C2), in effect order, each on the targets
    /// the ability found in the Attack phase, in that order. A target must still be standing and in range (or
    /// be the caster), and for a condition that needs a hit, the same activation must have hit it.
    /// </summary>
    private static void ResolveAbilityConditions(RoundContext round, string casterId, ActiveAbility active)
    {
        var ability = active.Ability;
        var targets = round.TickTargets.GetValueOrDefault(casterId) ?? [];
        var hits = round.TickHits.GetValueOrDefault(casterId);
        foreach (var effect in ability.ConditionEffects)
        {
            foreach (var targetId in targets)
            {
                // An attack an Intercept took (3B.6 PR 3): its condition follows it, with the range waived like the attack's.
                bool intercepted = round.TickRedirects.GetValueOrDefault(casterId) == targetId;
                var wasted = CheckConditionTarget(round, casterId, targetId, ability.Targeting, waiveRange: intercepted);
                if (wasted == WastedReason.None && effect.RequiresHit && hits?.Contains(targetId) != true)
                    wasted = WastedReason.Missed;
                if (wasted != WastedReason.None)
                    round.TickConditions.Add(new ConditionOutcome(
                        casterId, targetId, effect.Condition.Id, ability.Id, ConditionResultKind.Wasted, 0, wasted));
                else if (Resists(round, casterId, targetId, effect.Condition))
                    round.TickConditions.Add(new ConditionOutcome(
                        casterId, targetId, effect.Condition.Id, ability.Id, ConditionResultKind.Resisted, 0));
                else
                    round.TickConditions.Add(round.ApplyCondition(casterId, targetId, effect.Condition, effect.Duration, ability.Id));
            }
        }
    }

    /// <summary>
    /// ConditionResist (SPRINT_3B4 C9): a harmful condition from an enemy is resisted when the target's d100
    /// comes up at or under its <see cref="CombatProfile.ConditionResist"/>. The d100 is only rolled when that
    /// is above 0, so combatants without resistance never change the dice sequence; beneficial conditions and
    /// conditions from allies or yourself are never rolled against.
    /// </summary>
    private static bool Resists(RoundContext round, string casterId, string targetId, ConditionDefinition condition)
    {
        var target = round.Combatants[targetId];
        int resist = target.Profile.ConditionResist;
        if (!condition.Harmful || resist <= 0) return false;
        if (Relations.Between(round.Combatants[casterId], target) != Relation.Enemy) return false;
        return round.Dice.Roll(100) <= resist;
    }

    /// <summary>
    /// Whether a condition can land on a target right now (the condition side of <see cref="CheckAttackTarget"/>):
    /// the target exists → it's on the side the targeting allows → it's standing (conditions never go on
    /// someone who is down, SPRINT_3B4 C10) → it's within range of the caster (the caster itself always is).
    /// </summary>
    private static WastedReason CheckConditionTarget(
        RoundContext round, string casterId, string targetId, Targeting targeting, bool waiveRange = false)
    {
        if (!round.Combatants.TryGetValue(targetId, out var target))
            return WastedReason.InvalidTarget;
        if (casterId == targetId)
            return round.IsOut(targetId) ? WastedReason.TargetDefeated : WastedReason.None;
        if (!Relations.Matches(round.Combatants[casterId], target, targeting))
            return WastedReason.InvalidTargetType;
        if (round.IsOut(targetId))
            return WastedReason.TargetDefeated;
        if (!waiveRange && !round.WithinRange(casterId, targetId, targeting.Range))
            return WastedReason.OutOfRange;
        return WastedReason.None;
    }

    /// <summary>
    /// Stabilize (3B.5): an adjacent Downed target stops bleeding out and becomes Stable. Runs in
    /// plan order, so of two stabilizers on one target the second finds it already Stable.
    /// </summary>
    private static EffectOutcome ResolveStabilize(RoundContext round, string stabilizerId, string targetId)
    {
        var wasted = WastedReason.None;
        if (!round.Combatants.ContainsKey(targetId))
            wasted = WastedReason.InvalidTarget;
        else if (round.Status[targetId] != CombatantStatus.Downed)
            wasted = WastedReason.TargetNotDowned;
        else if (!round.InMeleeReach(stabilizerId, targetId))
            wasted = WastedReason.OutOfMeleeReach;

        if (wasted == WastedReason.None)
        {
            round.SetStatus(targetId, CombatantStatus.Stable);
            round.Bleedout[targetId] = 0;
        }
        return new EffectOutcome(stabilizerId, EffectType.Stabilize, targetId, wasted);
    }

    // ----- End of round -----

    /// <summary>
    /// Periodic effects at the start of the end of round (SPRINT_3B4 C11): every standing combatant rolls its
    /// conditions' periodic damage and healing, in combatant order, then the order the conditions were applied
    /// (damage dice go into the dice sequence here). Damage goes through the bearer's own damage-taken
    /// modifiers with the condition's tags. Everything is netted per combatant as one batch, capped at MaxHp,
    /// then the same 0-HP check as a tick runs: whoever falls here fell this round, so their bleedout doesn't
    /// count down yet, and falling clears their conditions.
    /// </summary>
    private static List<PeriodicOutcome> ResolvePeriodic(RoundContext round)
    {
        var rolled = new List<(string CombatantId, ActiveCondition Condition, PeriodicEffect Effect, int Amount, int ConditionDamage)>();
        var deltas = new Dictionary<string, int>();
        foreach (var c in round.State.Combatants)
        {
            if (round.IsOut(c.Id)) continue;
            var conditions = round.Conditions[c.Id];
            foreach (var condition in conditions)
            {
                if (condition.Definition.Modifiers.Periodic is not { } effect) continue;
                int raw = Math.Max(0, (effect.Die is { } die ? round.Dice.Roll(die.Sides) : 0) + effect.Bonus);
                int amount = raw, conditionDamage = 0;
                if (effect.Kind == PeriodicKind.Damage)
                {
                    amount = ConditionMath.ApplyDamageTaken(raw, conditions, condition.Definition.Tags);
                    conditionDamage = amount - raw;
                }
                rolled.Add((c.Id, condition, effect, amount, conditionDamage));
                deltas[c.Id] = deltas.GetValueOrDefault(c.Id) + (effect.Kind == PeriodicKind.Heal ? amount : -amount);
            }
        }
        if (rolled.Count == 0) return [];

        var hpBefore = new Dictionary<string, int>(round.Hp);
        foreach (var (id, delta) in deltas)
            round.Hp[id] = Math.Min(round.Hp[id] + delta, round.Combatants[id].MaxHp);

        var fell = new HashSet<string>();
        ResolveFalls(round, fell);

        return rolled.Select(r => new PeriodicOutcome(
            r.CombatantId,
            r.Condition.Id,
            r.Effect.Kind,
            r.Amount,
            HpBefore: hpBefore[r.CombatantId],
            HpAfter: round.Hp[r.CombatantId],
            Status: round.Status[r.CombatantId],
            Fell: fell.Contains(r.CombatantId),
            ConditionDamage: r.ConditionDamage)).ToList();
    }

    /// <summary>
    /// Bleedout countdown (3B.5): everyone who was Downed when the round started and still is
    /// loses a round; at 0 they die. The round a combatant falls in doesn't count.
    /// </summary>
    private static List<BleedoutOutcome> ResolveBleedout(RoundContext round)
    {
        var results = new List<BleedoutOutcome>();
        foreach (var c in round.State.Combatants)
        {
            if (!round.DownedAtStart.Contains(c.Id) || round.Status[c.Id] != CombatantStatus.Downed) continue;
            int left = Math.Max(0, round.Bleedout[c.Id] - 1);
            round.Bleedout[c.Id] = left;
            if (left == 0) round.SetStatus(c.Id, CombatantStatus.Dead);
            results.Add(new BleedoutOutcome(c.Id, left, left == 0));
        }
        return results;
    }

    /// <summary>
    /// Cooldowns for the next round (ABILITIES.md A10): every existing cooldown goes down by one
    /// (and is dropped at 0), then each ability used this round is set to its full cooldown. Used in
    /// round 3 with cooldown 2: not plannable in rounds 4 and 5, ready again in round 6.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, int>> ResolveCooldowns(RoundContext round)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, int>>();
        foreach (var c in round.State.Combatants)
        {
            var next = new Dictionary<string, int>();
            foreach (var (abilityId, left) in c.Cooldowns)
                if (left > 1) next[abilityId] = left - 1;
            if (round.UsedAbilities.TryGetValue(c.Id, out var used))
                foreach (var abilityId in used)
                    if (c.Profile.FindAbility(abilityId) is { Cooldown: > 0 } ability)
                        next[abilityId] = ability.Cooldown;
            result[c.Id] = next.Count == 0 && c.Cooldowns.Count == 0 ? c.Cooldowns : next;
        }
        return result;
    }

    /// <summary>
    /// Conditions for the next round (SPRINT_3B4 C5): one with no rounds left is removed, any other goes down
    /// by one. Runs last, after bleedout and cooldowns.
    /// </summary>
    private static (Dictionary<string, IReadOnlyList<ActiveCondition>>, List<ExpiredCondition>) ResolveConditionCountdown(RoundContext round)
    {
        var next = new Dictionary<string, IReadOnlyList<ActiveCondition>>();
        var expired = new List<ExpiredCondition>();
        foreach (var c in round.State.Combatants)
        {
            var current = round.Conditions[c.Id];
            if (current.Count == 0)
            {
                next[c.Id] = current;
                continue;
            }
            next[c.Id] = ConditionMath.CountDown(current, out var gone);
            expired.AddRange(gone.Select(id => new ExpiredCondition(c.Id, id)));
        }
        return (next, expired);
    }

    // ----- Helpers -----

    /// <summary>
    /// The part of <paramref name="path"/> a move may legally walk: at most
    /// <paramref name="movementRange"/> steps, and never off the grid.
    /// </summary>
    private static List<GridPosition> LegalPrefix(List<GridPosition> path, EncounterState state, int movementRange)
    {
        int length = 1; // the start cell
        while (length < path.Count
               && length <= movementRange
               && IsOnGrid(path[length], state))
        {
            length++;
        }
        return length == path.Count ? path : path.GetRange(0, length);
    }

    private static bool IsOnGrid(GridPosition p, EncounterState state) =>
        p.X >= 0 && p.Y >= 0 && p.X < state.GridWidth && p.Y < state.GridHeight;

    private static PlannedAction? ActionAt(IReadOnlyList<PlannedAction> queue, int tick) =>
        tick < queue.Count ? queue[tick] : null; // exhausted queue → idle

    /// <summary>
    /// Shortest Chebyshev path, origin included. Steps diagonally while both axes differ,
    /// then straight (quirk 8 — TICK_RESOLUTION Decision 6 suggests preferring cardinal steps).
    /// </summary>
    internal static List<GridPosition> ComputeChebyshevPath(GridPosition start, GridPosition target)
    {
        var path = new List<GridPosition> { start };
        var current = start;
        while (current != target)
        {
            current = new GridPosition(
                current.X + Math.Sign(target.X - current.X),
                current.Y + Math.Sign(target.Y - current.Y));
            path.Add(current);
        }
        return path;
    }

    /// <summary>An ability going ahead this tick: its definition, the queued action, and its entry in <see cref="RoundContext.TickUses"/>.</summary>
    private sealed record ActiveAbility(AbilityDefinition Ability, PlannedAction Action, int UseIndex);

    private sealed class Mover(string combatantId, List<GridPosition> fullPath, int speed)
    {
        public string CombatantId { get; } = combatantId;
        public List<GridPosition> FullPath { get; } = fullPath;
        public int Speed { get; } = speed;
        /// <summary>The ability moving this combatant (Dash, Charge), or null for a plain move.</summary>
        public string? AbilityId { get; init; }
        /// <summary>Steps taken count toward the round's shared movement budget.</summary>
        public bool UsesBudget { get; init; } = true;
        /// <summary>Provokes no passive opportunity attacks (Disengage, SPRINT_3B6 X4).</summary>
        public bool Disengaging { get; init; }
        public int CurrentStep { get; set; }
        public bool Stopped { get; set; }
        /// <summary>A reaction dropped it mid-path (it also has <see cref="Stopped"/>).</summary>
        public bool Fell { get; set; }
        public string? BlockedBy { get; set; }
    }

    /// <summary>Private working copy of the round's mutable state; the caller's inputs are never touched.</summary>
    private sealed class RoundContext
    {
        public EncounterState State { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<PlannedAction>> Plans { get; }
        public IDiceRoller Dice { get; }
        public RulesConfig Config { get; }
        public Dictionary<string, CombatantState> Combatants { get; }

        /// <summary>Occupied cells. Combatants with no map position are absent until they move.</summary>
        public Dictionary<string, GridPosition> Positions { get; }
        public Dictionary<string, int> Hp { get; }

        /// <summary>Cells each combatant has stepped this round (the shared movement budget).</summary>
        public Dictionary<string, int> CellsMoved { get; } = [];

        /// <summary>Cells each caster's ability move covered in the current tick (Charge's bonus reads it).</summary>
        public Dictionary<string, int> AbilityCellsMoved { get; } = [];

        /// <summary>Everyone's status as the round goes (3B.5).</summary>
        public Dictionary<string, CombatantStatus> Status { get; }

        /// <summary>Bleedout rounds left, for Downed combatants.</summary>
        public Dictionary<string, int> Bleedout { get; }

        /// <summary>Downed when the round started: their bleedout counts down at its end.</summary>
        public HashSet<string> DownedAtStart { get; }

        /// <summary>Downed or Stable when the round started: the only ones a finishing blow can hit.</summary>
        public HashSet<string> OutAtStart { get; }

        public List<string> DefeatedThisRound { get; } = [];

        /// <summary>Abilities each combatant has used so far this round (they start cooldowns at its end).</summary>
        public Dictionary<string, HashSet<string>> UsedAbilities { get; } = [];

        /// <summary>The abilities going ahead in the current tick, by caster (set at the start of each tick).</summary>
        public Dictionary<string, ActiveAbility> TickAbilities { get; } = [];

        /// <summary>Every ability activation of the current tick, in plan order (auto targets are filled in during the attack phase).</summary>
        public List<AbilityUse> TickUses { get; } = [];

        /// <summary>Everyone's conditions as the round goes (3B.4).</summary>
        public Dictionary<string, IReadOnlyList<ActiveCondition>> Conditions { get; }

        /// <summary>Combatants a condition stops from acting in the current tick (Stunned, C8).</summary>
        public HashSet<string> TickStunned { get; } = [];

        /// <summary>Actions conditions stopped in the current tick, in order.</summary>
        public List<BlockedAction> TickBlocked { get; } = [];

        /// <summary>
        /// Each combatant's movement budget for the round: its profile's range changed by the conditions it had
        /// when the round started (C6). Conditions applied during the round don't change it.
        /// </summary>
        public Dictionary<string, int> MovementRange { get; }

        /// <summary>Reactions in the current tick, in resolution order (3B.6).</summary>
        public List<ReactionOutcome> TickReactions { get; } = [];

        /// <summary>
        /// Prepared reactions watching right now (SPRINT_3B6 X8, X16): watcher → its prepared ability. One entered at the
        /// start of its tick and leaves when it fires, the watcher falls or is Stunned, or the round ends.
        /// </summary>
        public Dictionary<string, AbilityDefinition> Watching { get; } = [];

        /// <summary>Prepared reactions that started watching in the current tick, in plan order.</summary>
        public List<PreparedReaction> TickWatching { get; } = [];

        /// <summary>Combatants who have made their one passive reaction this tick (SPRINT_3B6 X5).</summary>
        public HashSet<string> PassiveUsedThisTick { get; } = [];

        /// <summary>Conditions applied in the current tick, in order.</summary>
        public List<ConditionOutcome> TickConditions { get; } = [];

        /// <summary>The targets each ability found in the current tick's Attack phase, by caster.</summary>
        public Dictionary<string, IReadOnlyList<string>> TickTargets { get; } = [];

        /// <summary>Abilities whose attack an Intercept took in the current tick: caster → the interceptor (3B.6 PR 3).</summary>
        public Dictionary<string, string> TickRedirects { get; } = [];

        /// <summary>The targets each ability's attacks hit in the current tick, by caster (for conditions that need a hit).</summary>
        public Dictionary<string, HashSet<string>> TickHits { get; } = [];

        /// <summary>
        /// Standing when the round started and hasn't fallen since: the only ones whose queues still
        /// run. Someone healed back up mid-round stays off it until the next round.
        /// </summary>
        private HashSet<string> Acting { get; }

        public RoundContext(
            EncounterState state,
            IReadOnlyDictionary<string, IReadOnlyList<PlannedAction>> plans,
            IDiceRoller dice,
            RulesConfig config)
        {
            State = state;
            Dice = dice;
            Config = config;
            Combatants = state.Combatants.ToDictionary(c => c.Id);
            Positions = state.Combatants
                .Where(c => c.Position.HasValue)
                .ToDictionary(c => c.Id, c => c.Position!.Value);
            Hp = state.Combatants.ToDictionary(c => c.Id, c => c.Hp);
            Status = state.Combatants.ToDictionary(c => c.Id, c => c.Status);
            Bleedout = state.Combatants.ToDictionary(c => c.Id, c => c.BleedoutRoundsLeft);
            // Someone who is already down carries no conditions (SPRINT_3B4 C10), whatever the input says.
            Conditions = state.Combatants.ToDictionary(c => c.Id, c => c.IsDefeated ? [] : c.Conditions);
            MovementRange = state.Combatants.ToDictionary(c => c.Id, c => ConditionMath.EffectiveMovementRange(c.Profile, Conditions[c.Id]));
            DownedAtStart = state.Combatants.Where(c => c.Status == CombatantStatus.Downed).Select(c => c.Id).ToHashSet();
            OutAtStart = state.Combatants
                .Where(c => c.Status is CombatantStatus.Downed or CombatantStatus.Stable).Select(c => c.Id).ToHashSet();
            Acting = state.Combatants.Where(c => !c.IsDefeated).Select(c => c.Id).ToHashSet();

            foreach (var id in plans.Keys)
                if (!Combatants.ContainsKey(id))
                    throw new ArgumentException($"Plan given for unknown combatant '{id}'.", nameof(plans));

            // Each queue is cut to the actions its combatant can afford (roadmap 3B.2), with the AP its
            // conditions left it at the start of the round (C6). PlanningService rejects over-budget plans,
            // so this only guards the engine.
            // A new insertion-ordered dictionary keeps plan order.
            var affordable = new Dictionary<string, IReadOnlyList<PlannedAction>>();
            foreach (var (id, queue) in plans)
            {
                int n = PlanValidator.AffordableCount(queue, ConditionMath.EffectiveProfile(Combatants[id].Profile, Conditions[id]), config);
                affordable[id] = n == queue.Count ? queue : queue.Take(n).ToList();
            }
            Plans = affordable;
        }

        /// <summary>Not standing (Downed, Stable or Dead).</summary>
        public bool IsOut(string id) => Status[id] != CombatantStatus.Standing;

        /// <summary>Its queued actions still run: standing since the start of the round.</summary>
        public bool CanAct(string id) => Acting.Contains(id);

        /// <summary>Its action at the current tick resolves: it can act and no condition stops it this tick (C8).</summary>
        public bool Acts(string id) => Acting.Contains(id) && !TickStunned.Contains(id);

        /// <summary>Changes a status, recording anyone who stops standing this round (their queue is void from then on).</summary>
        public void SetStatus(string id, CombatantStatus status)
        {
            if (Status[id] == CombatantStatus.Standing && status != CombatantStatus.Standing)
            {
                DefeatedThisRound.Add(id);
                Acting.Remove(id);
                Watching.Remove(id); // a watcher who falls stops watching (SPRINT_3B6 X8)
                Conditions[id] = []; // falling clears every condition (SPRINT_3B4 C10)
            }
            Status[id] = status;
        }

        /// <summary>
        /// Puts a condition on <paramref name="targetId"/> (or refreshes it, SPRINT_3B4 C12) and reports it.
        /// The caller has already checked the target can take it.
        /// </summary>
        public ConditionOutcome ApplyCondition(
            string sourceId, string targetId, ConditionDefinition condition, int duration, string? abilityId)
        {
            var updated = ConditionMath.Apply(Conditions[targetId], new ActiveCondition(condition, duration, sourceId), out bool refreshed);
            Conditions[targetId] = updated;
            int left = updated.First(c => c.Id == condition.Id).RoundsLeft;
            return new ConditionOutcome(sourceId, targetId, condition.Id, abilityId,
                refreshed ? ConditionResultKind.Refreshed : ConditionResultKind.Applied, left);
        }

        /// <summary>
        /// Both combatants are on the map and within melee reach. A combatant with no map
        /// position isn't on the map, so it can't make or receive melee attacks.
        /// </summary>
        public bool InMeleeReach(string attackerId, string targetId) => WithinRange(attackerId, targetId, Config.MeleeReach);

        /// <summary>Both combatants are on the map and at most <paramref name="range"/> cells apart.</summary>
        public bool WithinRange(string attackerId, string targetId, int range) =>
            Positions.TryGetValue(attackerId, out var a)
            && Positions.TryGetValue(targetId, out var t)
            && a.ChebyshevDistanceTo(t) <= range;
    }
}
