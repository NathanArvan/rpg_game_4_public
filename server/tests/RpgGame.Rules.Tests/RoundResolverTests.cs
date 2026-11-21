using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

public class RoundResolverTests
{
    // ----- Builders -----

    private static CombatantState C(
        string id, int x, int y, int hp = 20, int speed = 10, int die = 8, bool defeated = false,
        int move = 6, int melee = 0, int ranged = 0, int dodge = 10, int ap = CombatProfile.DefaultMaxAp) =>
        new(id, id, hp, new CombatProfile(20, move, speed, melee, ranged, dodge, new DamageDie(die), ap),
            new GridPosition(x, y), defeated);

    private static CombatantState OffMap(string id) => new(id, id, 20, CombatProfile.Default, null, false);

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
            new Plans().Add("A", PlannedAction.Melee("B")).Add("B", PlannedAction.Move(3, 0)),
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
            new ScriptedDice(10, 5));

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
            new ScriptedDice(10, 8)); // only A's tick-0 attack rolls

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
        // A's first move is blocked by W after 1 step, so 3 of A's 4 cells are left for the second move.
        var o = Resolve(State(C("A", 0, 0, move: 4), C("W", 2, 0)),
            new Plans().Add("A", PlannedAction.Move(3, 0), PlannedAction.Move(1, 3)),
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
}
