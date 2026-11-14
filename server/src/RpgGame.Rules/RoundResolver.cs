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

        // Ticks = longest queue. Always at least one tick, so a round with no plans still
        // produces one (empty) tick — quirk 11.
        int tickCount = Math.Max(1, plans.Count == 0 ? 0 : plans.Values.Max(q => q.Count));

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

        var next = state with
        {
            Combatants = state.Combatants.Select(c => c with
            {
                Hp = round.Hp[c.Id],
                Position = round.Positions.TryGetValue(c.Id, out var p) ? p : c.Position,
                IsDefeated = round.IsOut(c.Id),
            }).ToList(),
        };

        return new RoundOutcome(next, ticks, round.DefeatedThisRound.ToList());
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

            var start = round.PositionOf(combatantId);
            var path = ComputeChebyshevPath(start, target);
            if (path.Count <= 1) continue; // Already at target — no movement result (quirk 11)

            movers.Add(new Mover(combatantId, path, round.Combatants[combatantId].Speed));
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
        string AttackerId, string TargetId, ActionType Type, WastedReason Wasted, bool Hit, int Damage, bool IsCrit);

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

            // Validate against post-movement positions. Only targets defeated in an EARLIER
            // round or tick (or unknown ids — quirk 12) are wasted: damage within a tick is
            // simultaneous, so nobody is "already dead" mid-phase (quirk 6).
            var wasted = WastedReason.None;
            if (!round.Combatants.ContainsKey(targetId) || round.IsOut(targetId))
            {
                wasted = WastedReason.TargetDefeated;
            }
            else if (action.Type == ActionType.MeleeAttack
                     && round.PositionOf(attackerId).ChebyshevDistanceTo(round.PositionOf(targetId)) > round.Config.MeleeReach)
            {
                wasted = WastedReason.OutOfMeleeReach;
            }
            // Ranged attacks have unlimited range in 3A.

            bool hit = false, isCrit = false;
            int damage = 0;
            if (wasted == WastedReason.None)
            {
                (hit, damage, isCrit) = ComputeAttack(round.Combatants[attackerId], round.Dice, round.Config);
                if (hit)
                    hpDeltas[targetId] = hpDeltas.GetValueOrDefault(targetId, 0) - damage;
            }

            pending.Add(new PendingAttack(attackerId, targetId, action.Type, wasted, hit, damage, isCrit));
        }

        // Snapshot, then apply all deltas as one netted batch (DESIGN_SPIKE Decision 3).
        var hpBefore = new Dictionary<string, int>(round.Hp);
        foreach (var (targetId, delta) in hpDeltas)
            round.Hp[targetId] += delta;

        // Defeat check runs once, after ALL damage is applied — enables mutual kills
        // (DESIGN_SPIKE Decision 2). It covers every combatant, so one already at HP ≤ 0
        // but not yet flagged is defeated here even if unattacked (quirk 10).
        var defeatedThisTick = new HashSet<string>();
        foreach (var c in round.State.Combatants)
        {
            if (!round.IsOut(c.Id) && round.Hp[c.Id] <= 0)
            {
                round.MarkDefeated(c.Id);
                defeatedThisTick.Add(c.Id);
            }
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
            TargetDefeated: defeatedThisTick.Contains(a.TargetId),
            a.Wasted)).ToList();
    }

    /// <summary>
    /// Damage pipeline (DESIGN_SPIKE Decision 4). The slots exist now so items, abilities,
    /// stats and crits can fill them in later without restructuring.
    /// </summary>
    private static (bool Hit, int Damage, bool IsCrit) ComputeAttack(
        CombatantState attacker, IDiceRoller dice, RulesConfig config)
    {
        int hitRoll = dice.RollD20() + config.AccuracyBonus; // accuracyBonus: 0 in 3A
        if (hitRoll < config.HitThreshold)                   // targetDodge: 10 in 3A
            return (false, 0, false);

        int baseDamage = dice.Roll(attacker.Damage.Sides);   // class dice in 3A
        int weaponBonus = 0;                                 // items (3B+)
        int abilityModifier = 0;                             // abilities (3B.3+)
        int totalDamage = baseDamage + weaponBonus + abilityModifier;

        bool isCrit = false;                                 // crit system (3B+)
        if (isCrit) totalDamage *= 2;

        return (true, totalDamage, isCrit);
    }

    // ----- Phase 3: Effects -----

    private static List<EffectOutcome> ResolveEffectsPhase(RoundContext round, int tick)
    {
        var results = new List<EffectOutcome>();
        foreach (var (combatantId, queue) in round.Plans)
        {
            // Includes combatants defeated in this tick's attack phase.
            if (round.IsOut(combatantId)) continue;
            // Quirk 4: defend has no mechanical effect yet; it only produces an entry.
            if (ActionAt(queue, tick)?.Type == ActionType.Defend)
                results.Add(new EffectOutcome(combatantId, EffectType.Defend));
        }
        return results;
    }

    // ----- Helpers -----

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

        private readonly HashSet<string> _defeatedBefore;
        private readonly HashSet<string> _defeatedThisRoundSet = [];
        public List<string> DefeatedThisRound { get; } = [];

        public RoundContext(
            EncounterState state,
            IReadOnlyDictionary<string, IReadOnlyList<PlannedAction>> plans,
            IDiceRoller dice,
            RulesConfig config)
        {
            State = state;
            Plans = plans;
            Dice = dice;
            Config = config;
            Combatants = state.Combatants.ToDictionary(c => c.Id);
            Positions = state.Combatants
                .Where(c => c.Position.HasValue)
                .ToDictionary(c => c.Id, c => c.Position!.Value);
            Hp = state.Combatants.ToDictionary(c => c.Id, c => c.Hp);
            _defeatedBefore = state.Combatants.Where(c => c.IsDefeated).Select(c => c.Id).ToHashSet();

            foreach (var id in plans.Keys)
                if (!Combatants.ContainsKey(id))
                    throw new ArgumentException($"Plan given for unknown combatant '{id}'.", nameof(plans));
        }

        /// <summary>Defeated before this round, or earlier in it.</summary>
        public bool IsOut(string id) => _defeatedBefore.Contains(id) || _defeatedThisRoundSet.Contains(id);

        public void MarkDefeated(string id)
        {
            if (_defeatedThisRoundSet.Add(id))
                DefeatedThisRound.Add(id);
        }

        /// <summary>Quirk 5: a combatant with no map position is treated as standing at (0, 0).</summary>
        public GridPosition PositionOf(string id) => Positions.GetValueOrDefault(id, GridPosition.Origin);
    }
}
