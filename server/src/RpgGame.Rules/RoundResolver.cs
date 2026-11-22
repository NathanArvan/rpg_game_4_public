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
            var abilityUses = ResolveAbilityActivations(round, tick);
            var movements = ResolveMovementPhase(round, tick);
            var attacks = ResolveAttackPhase(round, tick);
            var effects = ResolveEffectsPhase(round, tick);
            ticks.Add(new TickOutcome(tick, movements, attacks, effects) { AbilityUses = abilityUses });
        }

        var endOfRound = ResolveBleedout(round);
        var cooldowns = ResolveCooldowns(round);

        var next = state with
        {
            Combatants = state.Combatants.Select(c => c with
            {
                Hp = round.Hp[c.Id],
                Position = round.Positions.TryGetValue(c.Id, out var p) ? p : c.Position,
                Status = round.Status[c.Id],
                BleedoutRoundsLeft = round.Status[c.Id] == CombatantStatus.Downed ? round.Bleedout[c.Id] : 0,
                Cooldowns = cooldowns[c.Id],
            }).ToList(),
        };

        return new RoundOutcome(next, ticks, round.DefeatedThisRound.ToList(), round.Plans, endOfRound);
    }

    // ----- Ability activations (start of each tick) -----

    /// <summary>
    /// Decides which abilities queued at this tick go ahead (3B.3). An ability is used once its tick
    /// is reached by a caster who is still standing, whatever then happens to its targets, and that
    /// starts its cooldown (ABILITIES.md A10). One that isn't in the loadout or is on cooldown is
    /// refused (planning rejects both; this only guards the engine). The ones that go ahead are
    /// recorded in <see cref="RoundContext.TickAbilities"/> for this tick's phases.
    /// </summary>
    private static List<AbilityUse> ResolveAbilityActivations(RoundContext round, int tick)
    {
        round.TickAbilities.Clear();
        var uses = new List<AbilityUse>();
        foreach (var (casterId, queue) in round.Plans)
        {
            if (round.IsOut(casterId)) continue;
            var action = ActionAt(queue, tick);
            if (action?.Type != ActionType.Ability) continue;

            var caster = round.Combatants[casterId];
            var ability = caster.Profile.FindAbility(action.AbilityId);
            var targets = TargetIdsOf(action);
            var abilityId = action.AbilityId ?? "";

            if (ability is null || ability.Activation != AbilityActivation.Active)
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
            round.TickAbilities[casterId] = ability;
            uses.Add(new AbilityUse(casterId, abilityId, targets, StartedCooldown: ability.Cooldown > 0));
        }
        return uses;
    }

    /// <summary>The chosen targets of an action: the single target, or the list (3B.3 PR 2).</summary>
    private static IReadOnlyList<string> TargetIdsOf(PlannedAction action) =>
        action.TargetCombatantIds is { Count: > 0 } list ? list
        : string.IsNullOrEmpty(action.TargetCombatantId) ? []
        : [action.TargetCombatantId];

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
        AttackRoll? Roll, bool Finishing, string? AbilityId);

    private static List<AttackOutcome> ResolveAttackPhase(RoundContext round, int tick)
    {
        var pending = new List<PendingAttack>();
        var hpDeltas = new Dictionary<string, int>(); // targetId → net delta (negative = damage)

        // Basic attacks and ability attacks share one batch: dice are rolled in plan order (then
        // target order within an ability), and all damage is netted together.
        foreach (var (attackerId, queue) in round.Plans)
        {
            if (round.IsOut(attackerId)) continue;
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
                    pending.Add(RollAttack(round, hpDeltas, attackerId, action.TargetCombatantId ?? "", action.Type,
                        melee ? AttackKind.Melee : AttackKind.Ranged, DamageSpec.Weapon, accuracyBonus: 0, wasted, abilityId: null));
                    break;
                }
                case ActionType.Ability when round.TickAbilities.TryGetValue(attackerId, out var ability):
                    foreach (var effect in ability.Effects.OfType<AttackEffect>())
                    {
                        var type = effect.Roll switch
                        {
                            AttackKind.Melee => ActionType.MeleeAttack,
                            AttackKind.Ranged => ActionType.RangedAttack,
                            _ => ActionType.Ability,
                        };
                        foreach (var targetId in TargetIdsOf(action))
                        {
                            var wasted = CheckAttackTarget(round, attackerId, targetId,
                                ability.Targeting, ability.Targeting.Range, WastedReason.OutOfRange);
                            pending.Add(RollAttack(round, hpDeltas, attackerId, targetId, type,
                                effect.Roll, effect.Damage, effect.AccuracyBonus, wasted, ability.Id));
                        }
                    }
                    break;
            }
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
            FinishingBlow: a.Finishing,
            AbilityId: a.AbilityId)).ToList();
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
    private static PendingAttack RollAttack(
        RoundContext round, Dictionary<string, int> hpDeltas, string attackerId, string targetId, ActionType type,
        AttackKind kind, DamageSpec damage, int accuracyBonus, WastedReason wasted, string? abilityId)
    {
        bool hit = false, isCrit = false, finishing = false;
        int amount = 0;
        AttackRoll? roll = null;
        if (wasted == WastedReason.None)
        {
            // Downed or Stable before this tick's attacks: any hit kills, with no damage roll.
            finishing = round.Status[targetId] != CombatantStatus.Standing;
            (hit, amount, isCrit, roll) = ComputeAttack(
                round.Combatants[attackerId], round.Combatants[targetId], kind, damage, accuracyBonus, round.Dice,
                rollDamage: !finishing);
            if (hit && !finishing)
                hpDeltas[targetId] = hpDeltas.GetValueOrDefault(targetId, 0) - amount;
        }
        return new PendingAttack(attackerId, targetId, type, wasted, hit, amount, isCrit, roll, finishing, abilityId);
    }

    /// <summary>
    /// Damage pipeline (DESIGN_SPIKE Decision 4, MAGIC_SYSTEM.md). The slots exist now so items,
    /// stats, resonance and crits can fill them in later without restructuring.
    /// </summary>
    /// <returns>Roll is null for an attack that can't miss (<see cref="AttackKind.None"/>).</returns>
    private static (bool Hit, int Damage, bool IsCrit, AttackRoll? Roll) ComputeAttack(
        CombatantState attacker, CombatantState target, AttackKind kind, DamageSpec spec, int accuracyBonus,
        IDiceRoller dice, bool rollDamage = true)
    {
        AttackRoll? roll = null;
        if (kind != AttackKind.None)
        {
            int accuracy = (kind == AttackKind.Melee ? attacker.Profile.MeleeAccuracy : attacker.Profile.RangedAccuracy)
                + accuracyBonus;
            roll = new AttackRoll(dice.RollD20(), accuracy, target.Profile.Dodge);
            if (roll.Total < roll.TargetDodge)
                return (false, 0, false, roll);
        }
        if (!rollDamage)
            return (true, 0, false, roll); // a finishing blow: the hit is what counts

        int baseDamage = dice.Roll((spec.Die ?? attacker.Profile.BaseDamage).Sides);
        int statModifier = 0;                                // stats (deferred)
        int weaponBonus = 0;                                 // items (4.2)
        int abilityModifier = spec.Bonus;                    // abilities (3B.3)
        int resonanceBonus = 0;                              // resonance (3M.5)
        int leyBonus = 0;                                    // ley points (3M.5)
        int totalDamage = (baseDamage + statModifier + weaponBonus + abilityModifier + resonanceBonus + leyBonus)
            * spec.Multiplier;

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

        /// <summary>Abilities each combatant has used so far this round (they start cooldowns at its end).</summary>
        public Dictionary<string, HashSet<string>> UsedAbilities { get; } = [];

        /// <summary>The abilities going ahead in the current tick, by caster (set at the start of each tick).</summary>
        public Dictionary<string, AbilityDefinition> TickAbilities { get; } = [];

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
                int n = PlanValidator.AffordableCount(queue, Combatants[id].Profile, config);
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
        public bool InMeleeReach(string attackerId, string targetId) => WithinRange(attackerId, targetId, Config.MeleeReach);

        /// <summary>Both combatants are on the map and at most <paramref name="range"/> cells apart.</summary>
        public bool WithinRange(string attackerId, string targetId, int range) =>
            Positions.TryGetValue(attackerId, out var a)
            && Positions.TryGetValue(targetId, out var t)
            && a.ChebyshevDistanceTo(t) <= range;
    }
}
