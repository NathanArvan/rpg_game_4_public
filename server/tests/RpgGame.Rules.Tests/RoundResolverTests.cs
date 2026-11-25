using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

public class RoundResolverTests
{
    // ----- Builders -----

    private static CombatantState C(
        string id, int x, int y, int hp = 20, int speed = 10, int die = 8, bool defeated = false,
        int move = 6, int melee = 0, int ranged = 0, int dodge = 10, int ap = CombatProfile.DefaultMaxAp) =>
        new(id, id, hp, new CombatProfile(20, move, speed, melee, ranged, dodge, new DamageDie(die), ap),
            new GridPosition(x, y), defeated ? CombatantStatus.Dead : CombatantStatus.Standing);

    private static CombatantState OffMap(string id) => new(id, id, 20, CombatProfile.Default, null);

    private static EncounterState State(params CombatantState[] combatants) => new(20, 20, combatants);

    private sealed class Plans : Dictionary<string, IReadOnlyList<PlannedAction>>
    {
        public Plans Add(string id, params PlannedAction[] queue) { this[id] = queue; return this; }
    }

    private static RoundOutcome Resolve(EncounterState state, Plans plans, ScriptedDice dice, RulesConfig? config = null)
    {
        var outcome = RoundResolver.Resolve(state, plans, dice, config);
        Assert.Equal(0, dice.Remaining);
        return outcome;
    }

    private static CombatantState Final(RoundOutcome o, string id) => o.Next.Combatants.Single(c => c.Id == id);
    private static GridPosition P(int x, int y) => new(x, y);

    // ----- Path stepping -----

    [Fact]
    public void Path_IsShortestChebyshev_DiagonalFirst()
    {
        Assert.Equal([P(0, 0), P(1, 1), P(2, 2), P(3, 2), P(4, 2)], RoundResolver.ComputeChebyshevPath(P(0, 0), P(4, 2)));
        Assert.Equal([P(5, 5), P(4, 4), P(4, 3)], RoundResolver.ComputeChebyshevPath(P(5, 5), P(4, 3)));
        Assert.Equal([P(2, 2)], RoundResolver.ComputeChebyshevPath(P(2, 2), P(2, 2)));
    }

    [Fact]
    public void Move_WalksFullPath_AndUpdatesNextState()
    {
        var o = Resolve(State(C("A", 0, 0)), new Plans().Add("A", PlannedAction.Move(3, 1)), new ScriptedDice());

        var mv = Assert.Single(Assert.Single(o.Ticks).Movements);
        Assert.Equal([P(0, 0), P(1, 1), P(2, 1), P(3, 1)], mv.Path);
        Assert.Equal(P(3, 1), mv.FinalPosition);
        Assert.False(mv.WasBlocked);
        Assert.Equal(P(3, 1), Final(o, "A").Position);
    }

    [Fact]
    public void Movers_StepInLockstep_ShortPathFinishesFirst()
    {
        // Independent movers of different path lengths both complete.
        var o = Resolve(State(C("A", 0, 0), C("B", 10, 10)),
            new Plans().Add("A", PlannedAction.Move(5, 0)).Add("B", PlannedAction.Move(10, 11)),
            new ScriptedDice());

        Assert.Equal(P(5, 0), Final(o, "A").Position);
        Assert.Equal(P(10, 11), Final(o, "B").Position);
    }

    [Fact]
    public void LongMover_BlockedByShortMoverThatArrivedEarlier()
    {
        // B finishes on step 1 at (2,0) and is then stationary; A reaches (2,0) on step 2.
        var o = Resolve(State(C("A", 0, 0), C("B", 2, 1)),
            new Plans().Add("A", PlannedAction.Move(4, 0)).Add("B", PlannedAction.Move(2, 0)),
            new ScriptedDice());

        var a = Assert.Single(o.Ticks).Movements.Single(m => m.CombatantId == "A");
        Assert.True(a.WasBlocked);
        Assert.Equal("B", a.BlockedById);
        Assert.Equal(P(1, 0), a.FinalPosition);
    }

    // ----- Contested cells -----

    [Fact]
    public void ContestedCell_HigherSpeedListedFirst_Wins()
    {
        var o = Resolve(State(C("Fast", 0, 0, speed: 14), C("Slow", 2, 0, speed: 10)),
            new Plans().Add("Fast", PlannedAction.Move(1, 0)).Add("Slow", PlannedAction.Move(1, 0)),
            new ScriptedDice());

        var mvs = Assert.Single(o.Ticks).Movements;
        Assert.False(mvs.Single(m => m.CombatantId == "Fast").WasBlocked);
        var slow = mvs.Single(m => m.CombatantId == "Slow");
        Assert.True(slow.WasBlocked);
        Assert.Equal("Fast", slow.BlockedById);
        Assert.Equal(P(1, 0), Final(o, "Fast").Position);
        Assert.Equal(P(2, 0), Final(o, "Slow").Position);
    }

    [Fact]
    public void ContestedCell_FasterWins_EvenWhenPlannedSecond()
    {
        var o = Resolve(State(C("Fast", 0, 0, speed: 14), C("Slow", 2, 0, speed: 10)),
            new Plans().Add("Slow", PlannedAction.Move(1, 0)).Add("Fast", PlannedAction.Move(1, 0)),
            new ScriptedDice());

        var mvs = Assert.Single(o.Ticks).Movements;
        Assert.False(mvs.Single(m => m.CombatantId == "Fast").WasBlocked);
        var slow = mvs.Single(m => m.CombatantId == "Slow");
        Assert.True(slow.WasBlocked);
        Assert.Equal("Fast", slow.BlockedById);
        Assert.Equal(P(1, 0), Final(o, "Fast").Position);
        Assert.Equal(P(2, 0), Final(o, "Slow").Position);
    }

    [Fact]
    public void ContestedCell_ThreeClaimants_FastestWins_OthersBlockedByIt()
    {
        var o = Resolve(State(C("Slow", 0, 0, speed: 8), C("Mid", 2, 0, speed: 10), C("Fast", 1, 1, speed: 14)),
            new Plans().Add("Slow", PlannedAction.Move(1, 0)).Add("Mid", PlannedAction.Move(1, 0)).Add("Fast", PlannedAction.Move(1, 0)),
            new ScriptedDice());

        var mvs = Assert.Single(o.Ticks).Movements;
        Assert.False(mvs.Single(m => m.CombatantId == "Fast").WasBlocked);
        foreach (var id in new[] { "Slow", "Mid" })
        {
            var m = mvs.Single(x => x.CombatantId == id);
            Assert.True(m.WasBlocked);
            Assert.Equal("Fast", m.BlockedById);
        }
        Assert.Equal(P(1, 0), Final(o, "Fast").Position);
        Assert.Equal(P(0, 0), Final(o, "Slow").Position);
        Assert.Equal(P(2, 0), Final(o, "Mid").Position);
    }

    [Fact]
    public void ContestedCell_TiedSpeed_AllClaimantsStop()
    {
        var o = Resolve(State(C("A", 0, 0), C("B", 2, 0)),
            new Plans().Add("A", PlannedAction.Move(1, 0)).Add("B", PlannedAction.Move(1, 0)),
            new ScriptedDice());

        var mvs = Assert.Single(o.Ticks).Movements;
        Assert.All(mvs, m => Assert.True(m.WasBlocked));
        Assert.Equal("B", mvs.Single(m => m.CombatantId == "A").BlockedById);
        Assert.Equal("A", mvs.Single(m => m.CombatantId == "B").BlockedById);
        Assert.Equal(P(0, 0), Final(o, "A").Position);
        Assert.Equal(P(2, 0), Final(o, "B").Position);
    }

    [Fact]
    public void ContestedCell_TieAmongTopTwo_StopsEveryClaimant()
    {
        var o = Resolve(State(C("A", 0, 0, speed: 12), C("B", 2, 0, speed: 12), C("C", 1, 1, speed: 5)),
            new Plans().Add("A", PlannedAction.Move(1, 0)).Add("B", PlannedAction.Move(1, 0)).Add("C", PlannedAction.Move(1, 0)),
            new ScriptedDice());

        Assert.All(Assert.Single(o.Ticks).Movements, m => Assert.True(m.WasBlocked));
    }

    // ----- Occupancy -----

    [Fact]
    public void Move_BlockedByStationaryOccupant()
    {
        var o = Resolve(State(C("A", 0, 0), C("Wall", 3, 0)),
            new Plans().Add("A", PlannedAction.Move(5, 0)),
            new ScriptedDice());

        var mv = Assert.Single(Assert.Single(o.Ticks).Movements);
        Assert.True(mv.WasBlocked);
        Assert.Equal("Wall", mv.BlockedById);
        Assert.Equal([P(0, 0), P(1, 0), P(2, 0)], mv.Path);
    }

    [Fact]
    public void Quirk9_DefeatedCombatantBlocksCell()
    {
        var o = Resolve(State(C("A", 0, 0), C("Corpse", 1, 0, hp: 0, defeated: true)),
            new Plans().Add("A", PlannedAction.Move(2, 0)),
            new ScriptedDice());

        Assert.Equal("Corpse", Assert.Single(Assert.Single(o.Ticks).Movements).BlockedById);
    }

    [Fact]
    public void Quirk2_MoversSwapCells()
    {
        var o = Resolve(State(C("A", 0, 0), C("B", 1, 0)),
            new Plans().Add("A", PlannedAction.Move(1, 0)).Add("B", PlannedAction.Move(0, 0)),
            new ScriptedDice());

        Assert.Equal(P(1, 0), Final(o, "A").Position);
        Assert.Equal(P(0, 0), Final(o, "B").Position);
    }

    [Fact]
    public void Move_BeyondMovementRange_StopsAfterRangeSteps()
    {
        var o = Resolve(State(C("A", 0, 0)), new Plans().Add("A", PlannedAction.Move(10, 0)), new ScriptedDice());

        var mv = Assert.Single(Assert.Single(o.Ticks).Movements);
        Assert.Equal(7, mv.Path.Count); // start + 6 steps
        Assert.Equal(P(6, 0), mv.FinalPosition);
        Assert.False(mv.WasBlocked);
        Assert.Equal(P(6, 0), Final(o, "A").Position);
    }

    [Fact]
    public void Move_RespectsEachCombatantsMovementRange()
    {
        var o = Resolve(State(C("Slow", 0, 0, move: 2), C("Fast", 0, 10, move: 8)),
            new Plans().Add("Slow", PlannedAction.Move(10, 0)).Add("Fast", PlannedAction.Move(10, 10)),
            new ScriptedDice());
        Assert.Equal(P(2, 0), Final(o, "Slow").Position);
        Assert.Equal(P(8, 10), Final(o, "Fast").Position);
    }

    [Fact]
    public void Move_OffGrid_StopsAtGridEdge()
    {
        var o = Resolve(State(C("A", 17, 1)), new Plans().Add("A", PlannedAction.Move(22, -4)), new ScriptedDice());

        // Diagonal-first path (18,0) → (19,-1) leaves the grid at the second step.
        var mv = Assert.Single(Assert.Single(o.Ticks).Movements);
        Assert.Equal([P(17, 1), P(18, 0)], mv.Path);
        Assert.Equal(P(18, 0), Final(o, "A").Position);
    }

    [Fact]
    public void NullPosition_CannotMove_OrMeleeOrBeMeleed_AndStaysNull()
    {
        var a = OffMap("A");
        var b = OffMap("B");
        var o = Resolve(new EncounterState(20, 20, [a, b, C("T", 1, 1), C("U", 0, 1)]),
            new Plans()
                .Add("A", PlannedAction.Move(2, 0))
                .Add("B", PlannedAction.Melee("T"))
                .Add("U", PlannedAction.Melee("A")),
            new ScriptedDice());

        Assert.Empty(o.Ticks[0].Movements);
        Assert.Null(Final(o, "A").Position);
        Assert.Null(Final(o, "B").Position);
        Assert.All(o.Ticks[0].Attacks, atk => Assert.Equal(WastedReason.OutOfMeleeReach, atk.WastedReason));
        Assert.Equal(2, o.Ticks[0].Attacks.Count);
    }

    [Fact]
    public void NullPosition_DoesNotBlockOrigin()
    {
        var ghost = OffMap("G");
        var o = Resolve(new EncounterState(20, 20, [ghost, C("A", 1, 0)]),
            new Plans().Add("A", PlannedAction.Move(0, 0)), new ScriptedDice());
        Assert.Equal(P(0, 0), Final(o, "A").Position);
    }

    [Fact]
    public void MoveToOwnCell_ReportsStayPut()
    {
        var o = Resolve(State(C("A", 4, 4)), new Plans().Add("A", PlannedAction.Move(4, 4)), new ScriptedDice());

        var mv = Assert.Single(Assert.Single(o.Ticks).Movements);
        Assert.Equal([P(4, 4)], mv.Path);
        Assert.Equal(P(4, 4), mv.FinalPosition);
        Assert.False(mv.WasBlocked);
        Assert.Null(mv.BlockedById);
    }

    [Fact]
    public void MoveToOwnCell_StillBlocksOtherMovers()
    {
        var o = Resolve(State(C("A", 4, 4), C("B", 2, 4)),
            new Plans().Add("A", PlannedAction.Move(4, 4)).Add("B", PlannedAction.Move(5, 4)),
            new ScriptedDice());

        var b = o.Ticks[0].Movements.Single(m => m.CombatantId == "B");
        Assert.True(b.WasBlocked);
        Assert.Equal("A", b.BlockedById);
        Assert.Equal(P(3, 4), b.FinalPosition);
    }

    // ----- Attacks -----

    [Fact]
    public void Attack_HitAtThreshold_MissBelow()
    {
        var hit = Resolve(State(C("A", 0, 0), C("B", 1, 0)), new Plans().Add("A", PlannedAction.Melee("B")), new ScriptedDice(10, 3));
        var atk = Assert.Single(hit.Ticks[0].Attacks);
        Assert.True(atk.Hit);
        Assert.Equal(3, atk.Damage);
        Assert.Equal((20, 17), (atk.TargetHpBefore, atk.TargetHpAfter));
        Assert.False(atk.IsCrit);
        Assert.Equal(17, Final(hit, "B").Hp);

        var miss = Resolve(State(C("A", 0, 0), C("B", 1, 0)), new Plans().Add("A", PlannedAction.Melee("B")), new ScriptedDice(9));
        var m = Assert.Single(miss.Ticks[0].Attacks);
        Assert.False(m.Hit);
        Assert.Equal(0, m.Damage);
        Assert.Equal(20, Final(miss, "B").Hp);
    }

    [Fact]
    public void Attack_UsesAttackersDamageDie()
    {
        var state = State(C("A", 0, 0, die: 4), C("B", 1, 0));
        Assert.Throws<InvalidOperationException>(() =>
            RoundResolver.Resolve(state, new Plans().Add("A", PlannedAction.Melee("B")), new ScriptedDice(10, 5)));
    }

    [Fact]
    public void Attack_HitsWhenRollPlusAccuracyReachesTargetDodge()
    {
        // 12 + 3 melee accuracy = 15 ≥ Dodge 15 → hit; ranged accuracy is ignored for melee.
        var hit = Resolve(State(C("A", 0, 0, melee: 3, ranged: -5), C("B", 1, 0, dodge: 15)),
            new Plans().Add("A", PlannedAction.Melee("B")), new ScriptedDice(12, 1));
        Assert.True(Assert.Single(hit.Ticks[0].Attacks).Hit);

        // 11 + 3 = 14 < 15 → miss.
        var miss = Resolve(State(C("A", 0, 0, melee: 3), C("B", 1, 0, dodge: 15)),
            new Plans().Add("A", PlannedAction.Melee("B")), new ScriptedDice(11));
        Assert.False(Assert.Single(miss.Ticks[0].Attacks).Hit);
    }

    [Fact]
    public void RangedAttack_UsesRangedAccuracy()
    {
        // 8 + 2 ranged = 10 ≥ Dodge 10 → hit, though melee accuracy (−5) would miss.
        var o = Resolve(State(C("A", 0, 0, melee: -5, ranged: 2), C("B", 5, 5)),
            new Plans().Add("A", PlannedAction.Ranged("B")), new ScriptedDice(8, 4));
        var atk = Assert.Single(o.Ticks[0].Attacks);
        Assert.True(atk.Hit);
        Assert.Equal(4, atk.Damage);
    }

    [Fact]
    public void Dodge_IsTheTargetsNotTheAttackers()
    {
        // Attacker's own Dodge 20 doesn't matter; the target's Dodge 5 does.
        var o = Resolve(State(C("A", 0, 0, dodge: 20), C("B", 1, 0, dodge: 5)),
            new Plans().Add("A", PlannedAction.Melee("B")), new ScriptedDice(5, 2));
        Assert.True(Assert.Single(o.Ticks[0].Attacks).Hit);
    }

    [Fact]
    public void RulesConfig_MeleeReachIsHonored()
    {
        var o = Resolve(State(C("A", 0, 0), C("B", 2, 2)), new Plans().Add("A", PlannedAction.Melee("B")),
            new ScriptedDice(10, 1), new RulesConfig { MeleeReach = 2 }); // distance 2 within reach 2

        var atk = Assert.Single(o.Ticks[0].Attacks);
        Assert.False(atk.Wasted);
        Assert.True(atk.Hit);
    }

    [Fact]
    public void MeleeReach_CheckedAfterMovement()
    {
        var away = Resolve(State(C("A", 0, 0), C("B", 1, 0)),
            new Plans().Add("A", PlannedAction.Melee("B")).Add("B", PlannedAction.Disengage(3, 0)), // no opportunity attack (3B.6)
            new ScriptedDice());
        Assert.Equal(WastedReason.OutOfMeleeReach, Assert.Single(away.Ticks[0].Attacks).WastedReason);

        var toward = Resolve(State(C("A", 0, 0), C("B", 4, 0)),
            new Plans().Add("A", PlannedAction.Melee("B")).Add("B", PlannedAction.Move(1, 1)),
            new ScriptedDice(10, 2));
        Assert.False(Assert.Single(toward.Ticks[0].Attacks).Wasted);
    }

    [Fact]
    public void RangedAttack_HasUnlimitedRange()
    {
        var o = Resolve(State(C("A", 0, 0), C("B", 19, 19)), new Plans().Add("A", PlannedAction.Ranged("B")), new ScriptedDice(10, 1));
        Assert.False(Assert.Single(o.Ticks[0].Attacks).Wasted);
    }

    [Fact]
    public void Attack_OnAlreadyDefeatedTarget_IsWasted_WithoutRolling()
    {
        var o = Resolve(State(C("A", 0, 0), C("B", 1, 0, hp: 0, defeated: true)),
            new Plans().Add("A", PlannedAction.Melee("B")), new ScriptedDice());

        var atk = Assert.Single(o.Ticks[0].Attacks);
        Assert.Equal(WastedReason.TargetDefeated, atk.WastedReason);
        Assert.False(atk.TargetDefeated);
        Assert.Empty(o.DefeatedThisRound);
    }

    [Fact]
    public void Attack_OnUnknownTarget_IsWastedAsInvalidTarget()
    {
        var o = Resolve(State(C("A", 0, 0)), new Plans().Add("A", PlannedAction.Melee("nobody")), new ScriptedDice());
        var atk = Assert.Single(o.Ticks[0].Attacks);
        Assert.Equal(WastedReason.InvalidTarget, atk.WastedReason);
        Assert.Equal((0, 0), (atk.TargetHpBefore, atk.TargetHpAfter));
    }

    [Fact]
    public void DefeatedAttacker_DoesNotAct()
    {
        var o = Resolve(State(C("A", 0, 0, hp: 0, defeated: true), C("B", 1, 0)),
            new Plans().Add("A", PlannedAction.Melee("B")), new ScriptedDice());
        Assert.Empty(o.Ticks[0].Attacks);
    }

    // ----- Simultaneity -----

    [Fact]
    public void Damage_IsNetted_DefeatCheckAfterAllDeltas()
    {
        var o = Resolve(State(C("A", 0, 0), C("B", 2, 0), C("T", 1, 0, hp: 10)),
            new Plans().Add("A", PlannedAction.Melee("T")).Add("B", PlannedAction.Melee("T")),
            new ScriptedDice(10, 6, 10, 4));

        var attacks = o.Ticks[0].Attacks;
        Assert.All(attacks, a => Assert.Equal((10, 0, true), (a.TargetHpBefore, a.TargetHpAfter, a.TargetDefeated)));
        Assert.Equal(["T"], o.DefeatedThisRound);
        Assert.True(Final(o, "T").IsDefeated);
    }

    [Fact]
    public void MutualKill_BothDefeatedSameTick()
    {
        var o = Resolve(State(C("A", 0, 0, hp: 3), C("B", 1, 0, hp: 3)),
            new Plans().Add("A", PlannedAction.Melee("B")).Add("B", PlannedAction.Melee("A")),
            new ScriptedDice(15, 5, 15, 5));

        Assert.Equal(["A", "B"], o.DefeatedThisRound);
        Assert.All(o.Ticks[0].Attacks, a => Assert.True(a.TargetDefeated));
        Assert.True(Final(o, "A").IsDefeated);
        Assert.True(Final(o, "B").IsDefeated);
    }

    [Fact]
    public void DefeatedOrder_FollowsCombatantOrder_NotAttackOrder()
    {
        var o = Resolve(State(C("A", 0, 0, hp: 1), C("B", 1, 0, hp: 1)),
            new Plans().Add("B", PlannedAction.Melee("A")).Add("A", PlannedAction.Melee("B")),
            new ScriptedDice(10, 1, 10, 1));
        Assert.Equal(["A", "B"], o.DefeatedThisRound);
    }

    [Fact]
    public void ZeroHpUnflagged_IsDefeatedWithoutAttack()
    {
        var o = Resolve(State(C("A", 0, 0, hp: 0)), new Plans(), new ScriptedDice());
        Assert.Equal(["A"], o.DefeatedThisRound);
    }

    // ----- Effects -----

    [Fact]
    public void Defend_ProducesEffect_ButNotWhenDefeatedThisTick()
    {
        var o = Resolve(State(C("A", 0, 0), C("B", 1, 0, hp: 2), C("D", 5, 5)),
            new Plans().Add("A", PlannedAction.Melee("B")).Add("B", PlannedAction.Defend()).Add("D", PlannedAction.Defend()),
            new ScriptedDice(13, 5)); // 13 beats B's Dodge 13 (10 + 3 from Guarded, 3B.4)

        var effect = Assert.Single(o.Ticks[0].Effects);
        Assert.Equal(new EffectOutcome("D", EffectType.Defend), effect);
    }

    // ----- Multi-tick queues -----

    [Fact]
    public void TickCount_IsLongestQueue_ShorterQueuesIdle()
    {
        var o = Resolve(State(C("A", 0, 0), C("B", 10, 10)),
            new Plans()
                .Add("A", PlannedAction.Move(1, 0), PlannedAction.Move(2, 0), PlannedAction.Move(3, 0))
                .Add("B", PlannedAction.Defend()),
            new ScriptedDice());

        Assert.Equal(3, o.Ticks.Count);
        Assert.Equal([0, 1, 2], o.Ticks.Select(t => t.Index));
        Assert.Single(o.Ticks[0].Effects);
        Assert.Empty(o.Ticks[1].Effects);
        Assert.All(o.Ticks, t => Assert.Single(t.Movements));
        Assert.Equal(P(3, 0), Final(o, "A").Position);
    }

    [Fact]
    public void NoPlans_OneEmptyTick()
    {
        var o = Resolve(State(C("A", 0, 0)), new Plans(), new ScriptedDice());
        var tick = Assert.Single(o.Ticks);
        Assert.Empty(tick.Movements);
        Assert.Empty(tick.Attacks);
        Assert.Empty(tick.Effects);
    }

    [Fact]
    public void LaterTicks_SeePositionsAndHpFromEarlierTicks()
    {
        // Tick 0: A steps adjacent. Tick 1: A hits B. Tick 2: A hits again from the new HP.
        var o = Resolve(State(C("A", 0, 0, ap: 5), C("B", 2, 0)),
            new Plans().Add("A", PlannedAction.Move(1, 0), PlannedAction.Melee("B"), PlannedAction.Melee("B")),
            new ScriptedDice(10, 5, 10, 4));

        Assert.Empty(o.Ticks[0].Attacks);
        Assert.Equal((20, 15), (o.Ticks[1].Attacks[0].TargetHpBefore, o.Ticks[1].Attacks[0].TargetHpAfter));
        Assert.Equal((15, 11), (o.Ticks[2].Attacks[0].TargetHpBefore, o.Ticks[2].Attacks[0].TargetHpAfter));
    }

    [Fact]
    public void DefeatedInEarlierTick_LaterActionsVoided_AndAttacksOnThemWasted()
    {
        var o = Resolve(State(C("A", 0, 0), C("B", 1, 0, hp: 2), C("C", 0, 1)),
            new Plans()
                .Add("A", PlannedAction.Melee("B"), PlannedAction.Defend())
                .Add("B", PlannedAction.Defend(), PlannedAction.Melee("A"), PlannedAction.Move(5, 5))
                .Add("C", PlannedAction.Defend(), PlannedAction.Melee("B")),
            new ScriptedDice(13, 8)); // only A's tick-0 attack rolls; 13 beats the Guarded B's Dodge 13 (3B.4)

        Assert.Equal(["B"], o.DefeatedThisRound);
        var t1 = o.Ticks[1];
        var wasted = Assert.Single(t1.Attacks); // B's attack is voided; C's attack on B is wasted
        Assert.Equal(("C", WastedReason.TargetDefeated, false), (wasted.AttackerId, wasted.WastedReason, wasted.TargetDefeated));
        Assert.Empty(o.Ticks[2].Movements); // B's move voided
        Assert.Equal(P(1, 0), Final(o, "B").Position);
    }

    // ----- Action points (3B.2) -----

    [Fact]
    public void OverBudgetQueue_IsCutToWhatTheCombatantCanAfford()
    {
        // Melee 2 + Melee 2 + Defend 1 = 5 AP > 4: the defend is dropped, so there are two ticks.
        var o = Resolve(State(C("A", 0, 0), C("B", 1, 0)),
            new Plans().Add("A", PlannedAction.Melee("B"), PlannedAction.Melee("B"), PlannedAction.Defend()),
            new ScriptedDice(1, 1));

        Assert.Equal(2, o.Ticks.Count);
        Assert.All(o.Ticks, t => Assert.Empty(t.Effects));
    }

    [Fact]
    public void Moves_ShareTheMovementRangeAcrossTheRound()
    {
        // Range 4: the first move uses 3 cells, so the second stops after 1 more.
        var o = Resolve(State(C("A", 0, 0, move: 4)),
            new Plans().Add("A", PlannedAction.Move(3, 0), PlannedAction.Move(6, 0)),
            new ScriptedDice());

        Assert.Equal(P(3, 0), o.Ticks[0].Movements[0].FinalPosition);
        Assert.Equal(P(4, 0), o.Ticks[1].Movements[0].FinalPosition);
        Assert.Equal(P(4, 0), Final(o, "A").Position);
    }

    [Fact]
    public void BlockedSteps_DoNotUseUpTheMovementBudget()
    {
        // A's first move is blocked by W after 1 step, so 3 of A's 4 cells are left for the second move
        // (a Disengage, so stepping out of W's reach provokes nothing, 3B.6).
        var o = Resolve(State(C("A", 0, 0, move: 4), C("W", 2, 0)),
            new Plans().Add("A", PlannedAction.Move(3, 0), PlannedAction.Disengage(1, 3)),
            new ScriptedDice());

        Assert.True(o.Ticks[0].Movements[0].WasBlocked);
        Assert.Equal(P(1, 0), o.Ticks[0].Movements[0].FinalPosition);
        Assert.Equal(P(1, 3), Final(o, "A").Position);
    }

    [Fact]
    public void ExhaustedMovementBudget_LaterMoveStaysPut()
    {
        var o = Resolve(State(C("A", 0, 0, move: 2)),
            new Plans().Add("A", PlannedAction.Move(2, 0), PlannedAction.Move(4, 0)),
            new ScriptedDice());

        var second = o.Ticks[1].Movements[0];
        Assert.Equal([P(2, 0)], second.Path);
        Assert.Equal(P(2, 0), Final(o, "A").Position);
    }

    // ----- Attack triggers (3B.6 PR 3) -----

    private static readonly AbilityDefinition Parry = new("parry", "Parry", 1, 1, ["melee"],
        new Targeting(TargetKind.Self, TargetFilter.Ally, 0), [new ModifyIncomingEffect(4)], Activation: AbilityActivation.Prepared)
    {
        Trigger = new ReactionTrigger(TriggerKind.Attacked, AttackKind: AttackKind.Melee),
    };

    private static readonly AbilityDefinition Intercept = new("intercept", "Intercept", 1, 2, ["melee"],
        new Targeting(TargetKind.Self, TargetFilter.Ally, 0), [new RedirectEffect()], Activation: AbilityActivation.Prepared)
    {
        Trigger = new ReactionTrigger(TriggerKind.AllyAttacked, Range: 1),
    };

    private static CombatantState WithAbility(CombatantState c, string team, params AbilityDefinition[] abilities) =>
        c with { Team = team, Profile = c.Profile with { Abilities = abilities } };

    [Fact]
    public void Parry_ShowsItsDodgeInTheRoll_AndIsReportedAtStepZeroWithNoAttack()
    {
        var o = Resolve(State(WithAbility(C("K", 5, 5), "party", Parry), WithAbility(C("G", 6, 5), "enemy")),
            new Plans().Add("K", PlannedAction.Ability("parry")).Add("G", PlannedAction.Melee("K")),
            new ScriptedDice(11));

        var tick = o.Ticks[0];
        var reaction = Assert.Single(tick.Reactions);
        Assert.Equal(("K", "G", ReactionKind.Prepared, "parry", 0), (reaction.ReactorId, reaction.TriggerId, reaction.Kind, reaction.AbilityId, reaction.Step));
        Assert.Null(reaction.Attack);
        Assert.Equal(4, reaction.ReactionDodge);
        var attack = Assert.Single(tick.Attacks);
        Assert.False(attack.Hit);
        Assert.Equal(new AttackRoll(11, 0, 14) { ReactionDodge = 4 }, attack.Roll);
        Assert.Empty(o.UnusedReactions);
    }

    [Fact]
    public void Intercept_MakesTheAttackOnTheWatcher_AndKeepsTheOriginalTarget()
    {
        var o = Resolve(State(
                WithAbility(C("B", 5, 5), "party", Intercept), WithAbility(C("M", 6, 5), "party"), WithAbility(C("G", 7, 5), "enemy")),
            new Plans().Add("B", PlannedAction.Ability("intercept")).Add("G", PlannedAction.Melee("M")),
            new ScriptedDice(12, 5));

        var tick = o.Ticks[0];
        var reaction = Assert.Single(tick.Reactions);
        Assert.Equal(("B", "G", "M"), (reaction.ReactorId, reaction.TriggerId, reaction.InterceptedFromId));
        var attack = Assert.Single(tick.Attacks);
        Assert.Equal(("B", "M", true, 5), (attack.TargetId, attack.InterceptedFromId, attack.Hit, attack.Damage));
        Assert.Equal((20, 15), (attack.TargetHpBefore, attack.TargetHpAfter));
        Assert.Equal(15, Final(o, "B").Hp);
        Assert.Equal(20, Final(o, "M").Hp);
    }

    [Fact]
    public void Intercept_IgnoresAnAttackFromAnAlly()
    {
        // Only an enemy's attack is taken: a party member hitting another (no reason, but basic attacks allow it) isn't.
        var o = Resolve(State(
                WithAbility(C("B", 5, 5), "party", Intercept), WithAbility(C("M", 6, 5), "party"), WithAbility(C("X", 7, 5), "party")),
            new Plans().Add("B", PlannedAction.Ability("intercept")).Add("X", PlannedAction.Melee("M")),
            new ScriptedDice(12, 5));

        Assert.Empty(o.Ticks[0].Reactions);
        Assert.Equal("M", Assert.Single(o.Ticks[0].Attacks).TargetId);
        Assert.Single(o.UnusedReactions);
    }

    // ----- Opportunity attacks (3B.6) -----

    [Fact]
    public void OpportunityAttack_IsReportedAtItsStep_AndTheAttackPhaseStartsFromTheHpItLeft()
    {
        // H steps out of G's reach on step 1 (hit for 4); Archer's ranged attack in the same tick's Attack phase
        // then starts from 16, so the two feed lines don't count the 4 twice (SPRINT_3B6 X3).
        var o = Resolve(State(C("H", 5, 5), C("G", 6, 5), C("Archer", 15, 15)),
            new Plans().Add("H", PlannedAction.Move(2, 5)).Add("Archer", PlannedAction.Ranged("H")),
            new ScriptedDice(12, 4, 11, 3));

        var tick = o.Ticks[0];
        var reaction = Assert.Single(tick.Reactions);
        Assert.Equal(("G", "H", ReactionKind.Opportunity, 1), (reaction.ReactorId, reaction.TriggerId, reaction.Kind, reaction.Step));
        Assert.Null(reaction.AbilityId);
        var oa = reaction.Attack!;
        Assert.Equal(ActionType.MeleeAttack, oa.AttackType);
        Assert.Equal((true, 4, 20, 16), (oa.Hit, oa.Damage, oa.TargetHpBefore, oa.TargetHpAfter));
        Assert.Equal(new AttackRoll(12, 0, 10), oa.Roll);

        var shot = Assert.Single(tick.Attacks);
        Assert.Equal((16, 13), (shot.TargetHpBefore, shot.TargetHpAfter));
        Assert.Equal(StopReason.None, tick.Movements[0].StopReason);
        Assert.Equal(13, Final(o, "H").Hp);
    }

    [Fact]
    public void MoverDroppedMidPath_StopsThere_WithStopReasonFell_AndIsNotReportedBlocked()
    {
        var o = Resolve(State(C("H", 5, 5, hp: 2), C("G", 6, 5)),
            new Plans().Add("H", PlannedAction.Move(1, 5)),
            new ScriptedDice(12, 5));

        var move = Assert.Single(o.Ticks[0].Movements);
        Assert.Equal(StopReason.Fell, move.StopReason);
        Assert.False(move.WasBlocked);
        Assert.Null(move.BlockedById);
        Assert.Equal([P(5, 5), P(4, 5)], move.Path);
        Assert.Equal(P(4, 5), Final(o, "H").Position);
        Assert.Equal(["H"], o.DefeatedThisRound);
    }

    [Fact]
    public void OpportunityAttack_UsesTheOpportunityTag_ForDamageTakenModifiers()
    {
        // A condition that halves damage from "opportunity" sources only.
        var wary = new ActiveCondition(new ConditionDefinition("wary", "Wary", false, [],
            new Modifiers(DamageTaken: new DamageTakenModifier(-50, 0, ["opportunity"]))), 0);
        var o = Resolve(State(C("H", 5, 5) with { Conditions = [wary] }, C("G", 6, 5)),
            new Plans().Add("H", PlannedAction.Move(3, 5)),
            new ScriptedDice(12, 8));
        Assert.Equal(16, Final(o, "H").Hp);
    }

    // ----- Purity -----

    [Fact]
    public void Resolve_DoesNotMutateInputs()
    {
        var combatants = new List<CombatantState> { C("A", 0, 0, hp: 3), C("B", 1, 0, hp: 3), C("M", 5, 5) };
        var state = new EncounterState(20, 20, combatants);
        var queueA = new List<PlannedAction> { PlannedAction.Melee("B") };
        var plans = new Plans().Add("A", queueA.ToArray()).Add("B", PlannedAction.Melee("A")).Add("M", PlannedAction.Move(8, 5));

        var combatantsBefore = combatants.ToList();
        var plansBefore = plans.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());

        var o = RoundResolver.Resolve(state, plans, new ScriptedDice(15, 5, 15, 5));

        Assert.Equal(combatantsBefore, state.Combatants); // records: value equality, same instances
        Assert.Equal(plansBefore.Keys, plans.Keys);
        foreach (var (id, queue) in plansBefore) Assert.Equal(queue, plans[id]);
        Assert.NotSame(state, o.Next);
        Assert.Equal(3, state.Combatants[0].Hp);
        Assert.Equal(P(5, 5), state.Combatants[2].Position);
        Assert.False(state.Combatants[1].IsDefeated);
    }

    [Fact]
    public void Resolve_IsDeterministicForSameSeed()
    {
        var state = State(C("A", 0, 0), C("B", 1, 0), C("C", 2, 1));
        var plans = new Plans()
            .Add("A", PlannedAction.Melee("B"), PlannedAction.Melee("B"))
            .Add("B", PlannedAction.Melee("A"), PlannedAction.Ranged("C"))
            .Add("C", PlannedAction.Ranged("A"), PlannedAction.Melee("B"));

        var o1 = RoundResolver.Resolve(state, plans, new SeededDice(123));
        var o2 = RoundResolver.Resolve(state, plans, new SeededDice(123));

        Assert.Equal(o1.Next.Combatants, o2.Next.Combatants);
        Assert.Equal(o1.Ticks.SelectMany(t => t.Attacks), o2.Ticks.SelectMany(t => t.Attacks));
    }

    [Fact]
    public void PlanForUnknownCombatant_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RoundResolver.Resolve(State(C("A", 0, 0)), new Plans().Add("ghost", PlannedAction.Defend()), new ScriptedDice()));
    }

    // ----- What the outcome reports (for playback) -----

    [Fact]
    public void Attack_RecordsItsHitCheck_AndAWastedAttackRollsNothing()
    {
        // A (melee +2) hits B (dodge 12) on 10 + 2; B's melee on C is out of reach, so it rolls nothing.
        var o = Resolve(
            State(C("A", 0, 0, melee: 2), C("B", 1, 0, dodge: 12), C("C", 9, 9)),
            new Plans().Add("A", PlannedAction.Melee("B")).Add("B", PlannedAction.Melee("C")),
            new ScriptedDice(10, 4));

        var attacks = Assert.Single(o.Ticks).Attacks;
        var hit = attacks.Single(a => a.AttackerId == "A");
        Assert.Equal(new AttackRoll(10, 2, 12), hit.Roll);
        Assert.Equal(12, hit.Roll!.Total);
        Assert.True(hit.Hit);

        var wasted = attacks.Single(a => a.AttackerId == "B");
        Assert.Equal(WastedReason.OutOfMeleeReach, wasted.WastedReason);
        Assert.Null(wasted.Roll);
    }

    [Fact]
    public void Outcome_ReportsTheQueuesItResolved_CutToTheirAp()
    {
        // 4 AP: melee (2) + melee (2) fit, the defend (1) doesn't.
        var plans = new Plans()
            .Add("A", PlannedAction.Melee("B"), PlannedAction.Melee("B"), PlannedAction.Defend())
            .Add("B", PlannedAction.Defend());
        var o = Resolve(State(C("A", 0, 0, ap: 4), C("B", 1, 0, dodge: 30)), plans, new ScriptedDice(1, 1));

        Assert.Equal(["A", "B"], o.ResolvedPlans.Keys);
        Assert.Equal([ActionType.MeleeAttack, ActionType.MeleeAttack], o.ResolvedPlans["A"].Select(a => a.Type));
        Assert.Single(o.ResolvedPlans["B"]);
    }

    // ----- Bleedout (3B.5) -----

    private static CombatantState Downable(string id, int x, int y, int hp = 20) =>
        C(id, x, y, hp) with { Profile = C(id, x, y).Profile with { CanBeDowned = true } };

    private static CombatantState DownedAt(string id, int x, int y, int roundsLeft) =>
        Downable(id, x, y, hp: 0) with { Status = CombatantStatus.Downed, BleedoutRoundsLeft = roundsLeft };

    [Fact]
    public void ZeroHp_DownsThoseWhoCan_AndKillsTheRest_HpStopsAtZero()
    {
        var o = Resolve(
            State(Downable("Hero", 0, 0, hp: 3), C("Goblin", 1, 0, hp: 2)),
            new Plans().Add("Hero", PlannedAction.Melee("Goblin")).Add("Goblin", PlannedAction.Melee("Hero")),
            new ScriptedDice(15, 5, 15, 6));

        Assert.Equal(CombatantStatus.Downed, Final(o, "Hero").Status);
        Assert.Equal(RulesConfig.Default.BleedoutRounds, Final(o, "Hero").BleedoutRoundsLeft);
        Assert.Equal(CombatantStatus.Dead, Final(o, "Goblin").Status);
        Assert.Equal(0, Final(o, "Hero").Hp);
        Assert.Equal(0, Final(o, "Goblin").Hp);
        Assert.Equal(["Goblin", "Hero"], o.DefeatedThisRound.Order());

        var attacks = Assert.Single(o.Ticks).Attacks;
        Assert.Equal(CombatantStatus.Downed, attacks.Single(a => a.TargetId == "Hero").TargetStatus);
        Assert.Equal(0, attacks.Single(a => a.TargetId == "Hero").TargetHpAfter);
        Assert.Empty(o.EndOfRound); // the round you fall in doesn't count down
    }

    [Fact]
    public void Bleedout_CountsDownAtEndOfRound_AndKillsAtZero()
    {
        var o = Resolve(State(DownedAt("A", 0, 0, roundsLeft: 2), DownedAt("B", 5, 5, roundsLeft: 1)), new Plans(), new ScriptedDice());

        Assert.Equal([new BleedoutOutcome("A", 1, false), new BleedoutOutcome("B", 0, true)], o.EndOfRound);
        Assert.Equal(CombatantStatus.Downed, Final(o, "A").Status);
        Assert.Equal(1, Final(o, "A").BleedoutRoundsLeft);
        Assert.Equal(CombatantStatus.Dead, Final(o, "B").Status);
        Assert.Empty(o.DefeatedThisRound); // both were already out of the fight
    }

    [Fact]
    public void Stabilize_IsWastedUnlessTheTargetIsDownedAndAdjacent()
    {
        var o = Resolve(
            State(DownedAt("Far", 9, 9, 2), C("Up", 1, 1), DownedAt("Near", 0, 1, 2), C("Medic", 0, 0), C("Medic2", 1, 0)),
            new Plans()
                .Add("Medic", PlannedAction.Stabilize("Near"))
                .Add("Medic2", PlannedAction.Stabilize("Near"))   // second on the same target: already Stable
                .Add("Up", PlannedAction.Stabilize("Far")),        // not adjacent
            new ScriptedDice());

        var effects = Assert.Single(o.Ticks).Effects;
        Assert.Equal(
            [("Medic", WastedReason.None), ("Medic2", WastedReason.TargetNotDowned), ("Up", WastedReason.OutOfMeleeReach)],
            effects.Select(e => (e.CombatantId, e.WastedReason)));
        Assert.All(effects, e => Assert.Equal(EffectType.Stabilize, e.Type));
        Assert.Equal(CombatantStatus.Stable, Final(o, "Near").Status);
        Assert.Equal(0, Final(o, "Near").BleedoutRoundsLeft);
        // Stabilized this round: no countdown; the unreached one counts down.
        Assert.Equal([new BleedoutOutcome("Far", 1, false)], o.EndOfRound);
    }

    [Fact]
    public void FinishingBlow_OnStable_RollsToHitOnly_AndKills()
    {
        var stable = Downable("S", 0, 1, hp: 0) with { Status = CombatantStatus.Stable };
        var o = Resolve(State(stable, C("Ogre", 0, 0)), new Plans().Add("Ogre", PlannedAction.Melee("S")), new ScriptedDice(10));

        var attack = Assert.Single(Assert.Single(o.Ticks).Attacks);
        Assert.True(attack.FinishingBlow);
        Assert.True(attack.Hit);
        Assert.Equal(0, attack.Damage);
        Assert.Equal(CombatantStatus.Dead, attack.TargetStatus);
        Assert.Equal(CombatantStatus.Dead, Final(o, "S").Status);
    }

    [Fact]
    public void QueuedSecondAttack_OnSomeoneWhoWentDownThisRound_IsWasted_NotAFinishingBlow()
    {
        // Finishing blows must be planned: the hero who falls in tick 1 survives the ogre's tick-2 attack,
        // so allies get a planning phase to react (3B.5).
        var o = Resolve(
            State(Downable("Hero", 0, 1, hp: 1), C("Ogre", 0, 0)),
            new Plans().Add("Ogre", PlannedAction.Melee("Hero"), PlannedAction.Melee("Hero")),
            new ScriptedDice(15, 3)); // tick 1 only: tick 2 rolls nothing

        Assert.Equal(WastedReason.TargetDefeated, Assert.Single(o.Ticks[1].Attacks).WastedReason);
        Assert.Equal(CombatantStatus.Downed, Final(o, "Hero").Status);
    }

    [Fact]
    public void AttackOnTheDead_IsWasted()
    {
        var o = Resolve(State(C("Dead", 0, 1, defeated: true), C("Ogre", 0, 0)),
            new Plans().Add("Ogre", PlannedAction.Melee("Dead")), new ScriptedDice());
        Assert.Equal(WastedReason.TargetDefeated, Assert.Single(Assert.Single(o.Ticks).Attacks).WastedReason);
    }

    [Fact]
    public void Downed_CannotAct()
    {
        var o = Resolve(State(DownedAt("D", 0, 0, 2), C("T", 0, 1)),
            new Plans().Add("D", PlannedAction.Melee("T")), new ScriptedDice());
        Assert.Empty(Assert.Single(o.Ticks).Attacks);
    }
}
