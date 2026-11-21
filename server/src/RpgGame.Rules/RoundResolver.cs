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
            // Combatants defeated in an earlier tick are skipped by IsOut(), which voids
            // their remaining queued actions.
            var movements = ResolveMovementPhase(round, tick);
            var attacks = ResolveAttackPhase(round, tick);
            var effects = ResolveEffectsPhase(round, tick);
            ticks.Add(new TickOutcome(tick, movements, attacks, effects));
        }

        var endOfRound = ResolveBleedout(round);

        var next = state with
        {
            Combatants = state.Combatants.Select(c => c with
            {
                Hp = round.Hp[c.Id],
                Position = round.Positions.TryGetValue(c.Id, out var p) ? p : c.Position,
                Status = round.Status[c.Id],
                BleedoutRoundsLeft = round.Status[c.Id] == CombatantStatus.Downed ? round.Bleedout[c.Id] : 0,
            }).ToList(),
        };

        return new RoundOutcome(next, ticks, round.DefeatedThisRound.ToList(), round.Plans, endOfRound);
    }

    // ----- Phase 1: Movement -----

    private static List<MovementOutcome> ResolveMovementPhase(RoundContext round, int tick)
    {
        var movers = new List<Mover>();
        foreach (var (combatantId, queue) in round.Plans)
        {
            if (round.IsOut(combatantId)) continue;
            var action = ActionAt(queue, tick);
            if (action?.Type != ActionType.Move || action.TargetCell is not { } target) continue;

            // A combatant with no map position isn't on the map, so it can't move.
            if (!round.Positions.TryGetValue(combatantId, out var start)) continue;

            // Re-check the move against the rules (PlanningService also validates it): the path
            // stops when the mover's movement budget for the round runs out (all moves share
            // MovementRange cells, roadmap 3B.2) or at the grid edge, whichever comes first.
            var mover = round.Combatants[combatantId];
            int cellsLeft = mover.Profile.MovementRange - round.CellsMoved.GetValueOrDefault(combatantId);
            var path = LegalPrefix(ComputeChebyshevPath(start, target), round.State, Math.Max(0, cellsLeft));

            // A move to the combatant's own cell is a mover with a one-cell path: it never steps,
            // blocks like any stationary occupant, and still reports a (stay-put) result.
            movers.Add(new Mover(combatantId, path, mover.Speed));
        }

        if (movers.Count == 0) return [];

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

            foreach (var mover in active)
            {
                if (blocked.Contains(mover.CombatantId))
                {
                    mover.Stopped = true;
                }
                else
                {
                    mover.CurrentStep++;
                    round.Positions[mover.CombatantId] = mover.FullPath[mover.CurrentStep];
                    // Only steps actually taken use up the movement budget.
                    round.CellsMoved[mover.CombatantId] = round.CellsMoved.GetValueOrDefault(mover.CombatantId) + 1;
                }
            }
        }

        return movers.Select(m => new MovementOutcome(
            m.CombatantId,
            m.FullPath.Take(m.CurrentStep + 1).ToList(),
            m.FullPath[m.CurrentStep],
            m.Stopped,
            m.BlockedBy)).ToList();
    }

    // ----- Phase 2: Attacks -----

    private sealed record PendingAttack(
        string AttackerId, string TargetId, ActionType Type, WastedReason Wasted, bool Hit, int Damage, bool IsCrit,
        AttackRoll? Roll, bool Finishing);

    private static List<AttackOutcome> ResolveAttackPhase(RoundContext round, int tick)
    {
        var pending = new List<PendingAttack>();
        var hpDeltas = new Dictionary<string, int>(); // targetId → net delta (negative = damage)

        foreach (var (attackerId, queue) in round.Plans)
        {
            if (round.IsOut(attackerId)) continue;
            var action = ActionAt(queue, tick);
            if (action?.Type is not (ActionType.MeleeAttack or ActionType.RangedAttack)) continue;

            var targetId = action.TargetCombatantId ?? "";

            // Validate against post-movement positions. Damage within a tick is simultaneous, so
            // nobody falls mid-phase (quirk 6). A target that fell in an EARLIER tick is wasted as
            // defeated — dead, or downed this round: finishing blows must be planned, so only
            // someone already Downed or Stable when the round began can be finished off (3B.5).
            // That way a queued second attack never kills a hero before allies can react.
            var wasted = WastedReason.None;
            bool finishing = false;
            if (!round.Combatants.ContainsKey(targetId))
            {
                wasted = WastedReason.InvalidTarget;
            }
            else if (round.Status[targetId] == CombatantStatus.Dead
                     || (round.IsOut(targetId) && !round.OutAtStart.Contains(targetId)))
            {
                wasted = WastedReason.TargetDefeated;
            }
            else if (action.Type == ActionType.MeleeAttack && !round.InMeleeReach(attackerId, targetId))
            {
                wasted = WastedReason.OutOfMeleeReach;
            }
            // Ranged attacks have unlimited range in 3A.

            bool hit = false, isCrit = false;
            int damage = 0;
            AttackRoll? roll = null;
            if (wasted == WastedReason.None)
            {
                // Downed or Stable before this tick's attacks: any hit kills, with no damage roll.
                finishing = round.Status[targetId] != CombatantStatus.Standing;
                (hit, damage, isCrit, roll) = ComputeAttack(
                    round.Combatants[attackerId], round.Combatants[targetId], action.Type, round.Dice, rollDamage: !finishing);
                if (hit && !finishing)
                    hpDeltas[targetId] = hpDeltas.GetValueOrDefault(targetId, 0) - damage;
            }

            pending.Add(new PendingAttack(attackerId, targetId, action.Type, wasted, hit, damage, isCrit, roll, finishing));
        }

        // Snapshot, then apply all deltas as one netted batch (DESIGN_SPIKE Decision 3).
        var hpBefore = new Dictionary<string, int>(round.Hp);
        foreach (var (targetId, delta) in hpDeltas)
            round.Hp[targetId] += delta;

        // Finishing blows land together with the damage: a Downed or Stable target that was hit dies.
        var fellThisTick = new HashSet<string>();
        foreach (var a in pending.Where(a => a.Finishing && a.Hit))
        {
            if (round.Status[a.TargetId] == CombatantStatus.Dead) continue;
            round.SetStatus(a.TargetId, CombatantStatus.Dead);
            fellThisTick.Add(a.TargetId);
        }

        // The 0-HP check runs once, after ALL damage is applied — enables mutual kills
        // (DESIGN_SPIKE Decision 2). It deliberately covers every combatant, not just this
        // tick's targets: anyone standing at HP ≤ 0 falls however they got there (e.g. future
        // damage-over-time statuses), even if nobody attacked them. Those who can be downed go
        // Downed and start bleeding out; everyone else dies (3B.5). HP stops at 0.
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
            fellThisTick.Add(c.Id);
        }

        return pending.Select(a => new AttackOutcome(
            a.AttackerId,
            a.TargetId,
            a.Type,
            a.Hit,
            a.Damage,
            a.IsCrit,
            TargetHpBefore: hpBefore.GetValueOrDefault(a.TargetId, 0),
            TargetHpAfter: round.Hp.GetValueOrDefault(a.TargetId, 0),
            TargetDefeated: fellThisTick.Contains(a.TargetId),
            a.Wasted,
            a.Roll,
            TargetStatus: round.Status.GetValueOrDefault(a.TargetId, CombatantStatus.Standing),
            FinishingBlow: a.Finishing)).ToList();
    }

    /// <summary>
    /// Damage pipeline (DESIGN_SPIKE Decision 4). The slots exist now so items, abilities,
    /// stats and crits can fill them in later without restructuring.
    /// </summary>
    private static (bool Hit, int Damage, bool IsCrit, AttackRoll Roll) ComputeAttack(
        CombatantState attacker, CombatantState target, ActionType type, IDiceRoller dice, bool rollDamage = true)
    {
        int accuracy = type == ActionType.MeleeAttack
            ? attacker.Profile.MeleeAccuracy
            : attacker.Profile.RangedAccuracy;
        var roll = new AttackRoll(dice.RollD20(), accuracy, target.Profile.Dodge);
        if (roll.Total < roll.TargetDodge)
            return (false, 0, false, roll);
        if (!rollDamage)
            return (true, 0, false, roll); // a finishing blow: the hit is what counts

        int baseDamage = dice.Roll(attacker.Profile.BaseDamage.Sides);
        int statModifier = 0;                                // stats (deferred)
        int weaponBonus = 0;                                 // items (4.2)
        int abilityModifier = 0;                             // abilities (3B.3+)
        int totalDamage = baseDamage + statModifier + weaponBonus + abilityModifier;

        bool isCrit = false;                                 // crit system (3B+)
        if (isCrit) totalDamage *= 2;

        return (true, totalDamage, isCrit, roll);
    }

    // ----- Phase 3: Effects -----

    private static List<EffectOutcome> ResolveEffectsPhase(RoundContext round, int tick)
    {
        var results = new List<EffectOutcome>();
        foreach (var (combatantId, queue) in round.Plans)
        {
            // Includes combatants who fell in this tick's attack phase.
            if (round.IsOut(combatantId)) continue;
            var action = ActionAt(queue, tick);
            switch (action?.Type)
            {
                case ActionType.Defend:
                    // Quirk 4: defend has no mechanical effect yet; it only produces an entry.
                    results.Add(new EffectOutcome(combatantId, EffectType.Defend));
                    break;
                case ActionType.Stabilize:
                    results.Add(ResolveStabilize(round, combatantId, action.TargetCombatantId ?? ""));
                    break;
            }
        }
        return results;
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

    private sealed class Mover(string combatantId, List<GridPosition> fullPath, int speed)
    {
        public string CombatantId { get; } = combatantId;
        public List<GridPosition> FullPath { get; } = fullPath;
        public int Speed { get; } = speed;
        public int CurrentStep { get; set; }
        public bool Stopped { get; set; }
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

        /// <summary>Everyone's status as the round goes (3B.5).</summary>
        public Dictionary<string, CombatantStatus> Status { get; }

        /// <summary>Bleedout rounds left, for Downed combatants.</summary>
        public Dictionary<string, int> Bleedout { get; }

        /// <summary>Downed when the round started: their bleedout counts down at its end.</summary>
        public HashSet<string> DownedAtStart { get; }

        /// <summary>Downed or Stable when the round started: the only ones a finishing blow can hit.</summary>
        public HashSet<string> OutAtStart { get; }

        public List<string> DefeatedThisRound { get; } = [];

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
            DownedAtStart = state.Combatants.Where(c => c.Status == CombatantStatus.Downed).Select(c => c.Id).ToHashSet();
            OutAtStart = state.Combatants
                .Where(c => c.Status is CombatantStatus.Downed or CombatantStatus.Stable).Select(c => c.Id).ToHashSet();

            foreach (var id in plans.Keys)
                if (!Combatants.ContainsKey(id))
                    throw new ArgumentException($"Plan given for unknown combatant '{id}'.", nameof(plans));

            // Each queue is cut to the actions its combatant can afford (roadmap 3B.2).
            // PlanningService rejects over-budget plans, so this only guards the engine.
            // A new insertion-ordered dictionary keeps plan order.
            var affordable = new Dictionary<string, IReadOnlyList<PlannedAction>>();
            foreach (var (id, queue) in plans)
            {
                int n = PlanValidator.AffordableCount(queue, Combatants[id].Profile.MaxAp, config);
                affordable[id] = n == queue.Count ? queue : queue.Take(n).ToList();
            }
            Plans = affordable;
        }

        /// <summary>Not standing (Downed, Stable or Dead): can't act this tick or later.</summary>
        public bool IsOut(string id) => Status[id] != CombatantStatus.Standing;

        /// <summary>Changes a status, recording anyone who stops standing this round.</summary>
        public void SetStatus(string id, CombatantStatus status)
        {
            if (Status[id] == CombatantStatus.Standing && status != CombatantStatus.Standing)
                DefeatedThisRound.Add(id);
            Status[id] = status;
        }

        /// <summary>
        /// Both combatants are on the map and within melee reach. A combatant with no map
        /// position isn't on the map, so it can't make or receive melee attacks.
        /// </summary>
        public bool InMeleeReach(string attackerId, string targetId) =>
            Positions.TryGetValue(attackerId, out var a)
            && Positions.TryGetValue(targetId, out var t)
            && a.ChebyshevDistanceTo(t) <= Config.MeleeReach;
    }
}
