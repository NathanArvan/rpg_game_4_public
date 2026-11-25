using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using static RpgGame.Api.Contracts.Hub.HubEvents;

namespace RpgGame.Api.IntegrationTests.Hub;

/// <summary>
/// Whole rounds over a real SignalR connection: hub → services → Redis / Postgres → broadcasts,
/// with exact (scripted) rolls. The presets are this project's <c>Presets/it-*.json</c>.
/// Every test ends by checking every broadcast it received against the client's TS contracts.
/// </summary>
[Collection(ApiCollection.Name)]
public class QuickBattleHubTests(ApiFactory api) : IAsyncLifetime
{
    private HubClient _client = null!;

    public async Task InitializeAsync() => _client = await HubClient.ConnectNewUserAsync(api);

    public async Task DisposeAsync() => await _client.DisposeAsync();

    private static PlannedActionArray Queue(params PlannedActionEntry[] actions) => new(actions);
    private static PlannedActionEntry Melee(string targetId) => new("melee_attack", null, targetId);
    private static PlannedActionEntry Ability(string abilityId, string targetId) => new("ability", null, targetId, abilityId);
    private static PlannedActionEntry Move(int x, int y) => new("move", new PositionDto(x, y), null);
    private static PlannedActionEntry Defend() => new("defend", null, null);

    private async Task<(QuickBattleStartedResult Battle, Dictionary<string, string> Ids)> StartAsync(string presetId)
    {
        var battle = await _client.InvokeAsync<QuickBattleStartedResult>(Methods.StartQuickBattle, presetId);
        var started = await _client.NextAsync<EncounterStartedPayload>(Events.EncounterStarted);
        await _client.NextAsync<PlanningPhaseStartedPayload>(Events.PlanningPhaseStarted, p => p.RoundNumber == 1);
        return (battle, started.Combatants.ToDictionary(c => c.CharacterName, c => c.Id));
    }

    [Fact]
    public async Task A_quick_battle_round_resolves_with_exact_rolls_and_persists()
    {
        var (battle, ids) = await StartAsync("it-duel");
        var (hero, goblin) = (ids["Hero"], ids["Goblin"]);

        // Round 1: Hero hits (d20 15 + 1 ≥ dodge 13: the manual Goblin defends, so it's Guarded, 3B.4) for 4.
        api.Dice.Script(15, 4);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Melee(goblin)), hero);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var round1 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        var attack = Assert.Single(round1.Ticks.SelectMany(t => t.Attacks));
        Assert.Equal((hero, goblin, true, 4, 6), (attack.AttackerId, attack.TargetId, attack.Hit, attack.Damage, attack.TargetNewHp));
        Assert.Equal(new AttackRollDto(15, 1, 13, 0, 3), attack.Roll);
        Assert.Equal(0, api.Dice.Remaining);

        var round2 = await _client.NextAsync<PlanningPhaseStartedPayload>(Events.PlanningPhaseStarted, p => p.RoundNumber == 2);
        Assert.Equal(6, round2.Combatants.Single(c => c.Id == goblin).CurrentHp);
        Assert.Equal(0, round2.PlanningTimerDurationSeconds); // Quick Battle: no timer
        // LockInEnemies announces the GM, not each monster.
        Assert.Equal(["Hero", "GM"], _client.All<PlayerLockedInPayload>(Events.PlayerLockedIn).Select(p => p.CharacterName));

        // Live state in the real Redis: monsters' HP is keyed by combatant id; the round moved on.
        Assert.Equal(6, await api.Redis.GetCharacterHpAsync(goblin));
        var active = await api.Redis.GetActiveEncounterAsync(battle.GameSessionId.ToString());
        Assert.Equal(("planning", 2), (active!.RoundPhase, active.RoundNumber));
        Assert.Equal(0, await api.Redis.GetLockedInCountAsync(active.EncounterId));
        Assert.Empty(await api.Redis.GetAllPlannedActionsAsync(active.EncounterId));

        // Round 2: Hero hits for 6 and the Goblin falls; the encounter ends and its Redis state goes.
        api.Dice.Script(15, 6);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Melee(goblin)), hero);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var final = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        Assert.Equal([goblin], final.DefeatedCombatantIds);
        Assert.Equal(goblin, (await _client.NextAsync<CombatantDefeatedPayload>(Events.CombatantDefeated)).CombatantId);
        var ended = await _client.NextAsync<EncounterEndedPayload>(Events.EncounterEnded);
        Assert.Equal(("party", 2), (ended.WinningSide, ended.FinalRoundNumber));

        Assert.Null(await api.Redis.GetActiveEncounterAsync(battle.GameSessionId.ToString()));
        var encounterId = Guid.Parse(active.EncounterId);
        var (status, eventTypes) = await api.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var encounter = await db.Encounters.SingleAsync(e => e.Id == encounterId);
            var types = await db.GameEvents.Where(e => e.EncounterId == encounterId).Select(e => e.EventType).ToListAsync();
            return (encounter.Status, types);
        });
        Assert.Equal(EncounterStatus.Ended, status);
        Assert.Contains("encounter_ended", eventTypes);

        ContractShape.AssertEventsMatch(_client.Events);
    }

    [Fact]
    public async Task Stepping_out_of_the_goblins_reach_draws_an_opportunity_attack_and_Disengage_doesnt()
    {
        // 3B.6 PR 1. it-duel: Hero (9,9) next to the manual Goblin (9,10), which defends (so it doesn't move and
        // can react). Round 1: Hero walks to (9,7); its first step leaves the Goblin's reach: d20 20 hits, d6 3.
        var (battle, ids) = await StartAsync("it-duel");
        var (hero, goblin) = (ids["Hero"], ids["Goblin"]);

        api.Dice.Script(20, 3);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Move(9, 7)), hero);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var round1 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        Assert.Equal(0, api.Dice.Remaining);
        var reaction = Assert.Single(round1.Ticks.SelectMany(t => t.Reactions ?? []));
        Assert.Equal((goblin, hero, "opportunity", 1), (reaction.ReactorId, reaction.TriggerId, reaction.Kind, reaction.Step));
        Assert.Equal((true, 3, 17), (reaction.Attack!.Hit, reaction.Attack.Damage, reaction.Attack.TargetNewHp));
        Assert.Equal("Goblin's opportunity attack on Hero hits for 3 damage (HP: 20 → 17)", reaction.FeedText);
        Assert.Equal("none", Assert.Single(round1.Ticks[0].Movements).StopReason);

        // Round 2: back next to the Goblin (stepping into reach is free), then Disengage away: no rolls at all.
        await _client.NextAsync<PlanningPhaseStartedPayload>(Events.PlanningPhaseStarted, p => p.RoundNumber == 2);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode,
            Queue(Move(9, 9), new PlannedActionEntry("disengage", new PositionDto(9, 7), null)), hero);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var round2 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted, p => p.NewRoundNumber == 3);
        Assert.Empty(round2.Ticks.SelectMany(t => t.Reactions ?? []));
        Assert.True(round2.Ticks[1].Movements.Single().Disengaging);
        Assert.Equal(("disengage", 2), (round2.Queues.Single(q => q.CombatantId == hero).Actions[1].Type,
            round2.Queues.Single(q => q.CombatantId == hero).Actions[1].ApCost));

        ContractShape.AssertEventsMatch(_client.Events);
    }

    [Fact]
    public async Task Brace_strikes_the_goblin_as_it_steps_into_reach_and_nothing_can_follow_it()
    {
        // 3B.6 PR 2. it-brace: Hero (9,9) with Brace; the manual Goblin (9,12) walks to (9,10), stepping into reach at
        // step 2. Brace: d20 20 hits; d6 3 + 2 = 5.
        var (battle, ids) = await StartAsync("it-brace");
        var (hero, goblin) = (ids["Hero"], ids["Goblin"]);
        var brace = new PlannedActionEntry("ability", null, null, "brace");

        // Anything after a prepared reaction is refused (SPRINT_3B6 X7).
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(brace, Defend()), hero);
        var rejected = await _client.NextAsync<ActionRejectedPayload>(Events.ActionRejected);
        Assert.Equal("A prepared reaction must be the last action in the queue.", rejected.Reason);

        api.Dice.Script(20, 3);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(brace), hero);
        await _client.InvokeAsync(Methods.PlanEnemyAction, battle.JoinCode, goblin, Queue(Move(9, 10)));
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var round1 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        Assert.Equal(0, api.Dice.Remaining);
        var watching = Assert.Single(round1.Ticks[0].Watching!);
        Assert.Equal((hero, "brace", "Hero prepares Brace"), (watching.CombatantId, watching.AbilityId, watching.FeedText));
        var reaction = Assert.Single(round1.Ticks[0].Reactions!);
        Assert.Equal((hero, goblin, "prepared", "brace", 2), (reaction.ReactorId, reaction.TriggerId, reaction.Kind, reaction.AbilityId, reaction.Step));
        Assert.Equal("Hero's Brace hits Goblin for 5 damage (HP: 10 → 5)", reaction.FeedText);
        Assert.Empty(round1.UnusedReactions!);
        Assert.Equal(1, round1.UpdatedCombatants.Single(c => c.Id == hero).Cooldowns!["brace"]);

        ContractShape.AssertEventsMatch(_client.Events);
    }

    [Fact]
    public async Task Mark_puts_conditions_on_the_goblin_that_change_the_next_hit_and_carry_into_the_next_round()
    {
        var (battle, ids) = await StartAsync("it-mark");
        var (hero, goblin) = (ids["Hero"], ids["Goblin"]);
        var encounterId = (await api.Redis.GetActiveEncounterAsync(battle.GameSessionId.ToString()))!.EncounterId;

        // Round 1: Mark (tick 1) makes the Goblin Exposed (-3 Dodge) and Marked (+2 damage taken); it also
        // defends, so it's Guarded (+3) all round. Tick 2's melee: d20 10 + 1 vs 10 + 3 - 3 = 10 hits,
        // d6 3 + 2 = 5.
        api.Dice.Script(10, 3);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Ability("mark", goblin), Melee(goblin)), hero);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var round1 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        Assert.Equal(0, api.Dice.Remaining);
        var conditions = round1.Ticks.SelectMany(t => t.Conditions ?? []).ToList();
        Assert.Equal([("guarded", "applied"), ("marked", "applied"), ("exposed", "applied")],
            conditions.Select(c => (c.ConditionId, c.Result)));
        var attack = Assert.Single(round1.Ticks.SelectMany(t => t.Attacks));
        Assert.Equal((5, 2), (attack.Damage, attack.ConditionDamage));
        Assert.Equal(new AttackRollDto(10, 1, 10, 0, 0), attack.Roll); // +3 Guarded and -3 Exposed cancel out
        Assert.Equal(["guarded"], round1.ExpiredConditions!.Select(x => x.ConditionId));

        // Next planning phase and real Redis: Marked and Exposed last through round 2 (0 rounds left after it).
        var round2 = await _client.NextAsync<PlanningPhaseStartedPayload>(Events.PlanningPhaseStarted, p => p.RoundNumber == 2);
        var goblinDto = round2.Combatants.Single(c => c.Id == goblin);
        Assert.Equal([("marked", 0), ("exposed", 0)], goblinDto.Conditions!.Select(c => (c.Condition.Id, c.RoundsLeft)));
        Assert.Equal([("marked", hero), ("exposed", hero)],
            (await api.Redis.GetConditionsAsync(encounterId, goblin)).Select(c => (c.Condition.Id, c.SourceId!)));

        // Round 2: nobody acts; at its end both expire and the Redis entry goes.
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Move(8, 9)), hero);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);
        var round2Result = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted, p => p.NewRoundNumber == 3);
        Assert.Equal(["marked", "exposed", "guarded"], round2Result.ExpiredConditions!.Select(x => x.ConditionId));
        Assert.Empty(await api.Redis.GetConditionsAsync(encounterId, goblin));

        ContractShape.AssertEventsMatch(_client.Events);
    }

    [Fact]
    public async Task Burning_kills_the_last_goblin_at_the_end_of_the_round_and_ends_the_encounter()
    {
        var (battle, ids) = await StartAsync("it-burn");
        var (hero, goblin) = (ids["Hero"], ids["Goblin"]);

        // Firebolt: d20 15 + 1 ≥ 13 (Guarded) hits for d8 1 + 1 = 2 (4 → 2) and sets the Goblin Burning;
        // at the end of the round Burning's d4 3 drops it after the last tick.
        api.Dice.Script(15, 1, 3);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Ability("firebolt", goblin)), hero);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var round1 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        Assert.Equal(0, api.Dice.Remaining);
        Assert.Equal(2, Assert.Single(round1.Ticks.SelectMany(t => t.Attacks)).TargetNewHp);
        var burn = Assert.Single(round1.Periodic!);
        Assert.Equal((goblin, "burning", 3, 0, "dead", true), (burn.CombatantId, burn.ConditionId, burn.Amount, burn.NewHp, burn.Status, burn.Fell));
        Assert.Equal("Goblin takes 3 damage from Burning — defeated!", burn.FeedText);
        Assert.Equal([goblin], round1.DefeatedCombatantIds);
        Assert.Equal(goblin, (await _client.NextAsync<CombatantDefeatedPayload>(Events.CombatantDefeated)).CombatantId);
        var ended = await _client.NextAsync<EncounterEndedPayload>(Events.EncounterEnded);
        Assert.Equal("party", ended.WinningSide);

        ContractShape.AssertEventsMatch(_client.Events);
    }

    [Fact]
    public async Task A_round_where_the_only_hero_is_stunned_resolves_without_input()
    {
        var (battle, ids) = await StartAsync("it-hex");
        var (hero, shaman) = (ids["Hero"], ids["Shaman"]);

        // Round 1: the AI Shaman Hexes the Hero in tick 1 (no roll: the Hero has no resist) and walks next to it.
        // Round 2: the Hero is Stunned, so it's locked in with nothing; the Shaman swings twice (d20 1, 1: misses).
        api.Dice.Script(1, 1);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Defend()), hero);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);

        var round1 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        var stun = round1.Ticks[0].Conditions!.Single(c => c.ConditionId == "stunned");
        Assert.Equal((shaman, hero, 1), (stun.SourceId, stun.TargetId, stun.RoundsLeft));
        Assert.Equal("Shaman's Hex: Hero is Stunned until the end of next round", stun.FeedText);

        var round2 = await _client.NextAsync<PlanningPhaseStartedPayload>(Events.PlanningPhaseStarted, p => p.RoundNumber == 2);
        var stunned = round2.Combatants.Single(c => c.Id == hero);
        Assert.Equal([("all", "stunned"), ("move", "stunned")], stunned.Blocks!.Select(b => (b.Kind, b.ConditionId)));

        // Nobody plans or locks in for round 2: it resolves on its own.
        var resolved2 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted, p => p.NewRoundNumber == 3);
        Assert.Equal(0, api.Dice.Remaining);
        Assert.Equal(2, resolved2.Ticks.SelectMany(t => t.Attacks).Count(a => a.AttackerId == shaman && !a.Hit));
        Assert.DoesNotContain(resolved2.Queues!, q => q.CombatantId == hero && q.Actions.Count > 0);
        Assert.Contains("Hero is no longer Stunned", resolved2.ExpiredConditions!.Select(x => x.FeedText));

        var round3 = await _client.NextAsync<PlanningPhaseStartedPayload>(Events.PlanningPhaseStarted, p => p.RoundNumber == 3);
        Assert.Empty(round3.Combatants.Single(c => c.Id == hero).Blocks!);

        ContractShape.AssertEventsMatch(_client.Events);
    }

    [Fact]
    public async Task An_ability_on_the_wrong_side_is_rejected_over_the_wire_and_not_stored()
    {
        var (battle, ids) = await StartAsync("it-revive");

        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Ability("mend", ids["Ogre"])), ids["Medic"]);

        var rejected = await _client.NextAsync<ActionRejectedPayload>(Events.ActionRejected);
        Assert.Equal("Mend can only target allies.", rejected.Reason);
        var active = await api.Redis.GetActiveEncounterAsync(battle.GameSessionId.ToString());
        Assert.False(await api.Redis.HasPlannedActionAsync(active!.EncounterId, ids["Medic"]));

        ContractShape.AssertEventsMatch(_client.Events);
    }

    [Fact]
    public async Task A_hero_healed_back_up_plans_again_the_next_round()
    {
        var (battle, ids) = await StartAsync("it-revive");
        var (medic, hero, ogre) = (ids["Medic"], ids["Hero"], ids["Ogre"]);
        var sessionId = battle.GameSessionId.ToString();
        var encounterId = (await api.Redis.GetActiveEncounterAsync(sessionId))!.EncounterId;

        // Round 1: the GM's Ogre hits the 1-HP Hero (d20 10, d10 5): Downed.
        api.Dice.Script(10, 5);
        await _client.InvokeAsync(Methods.PlanEnemyAction, battle.JoinCode, ogre, Queue(Melee(hero)));
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Defend()), medic);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var round1 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        Assert.Equal([hero], round1.DefeatedCombatantIds);
        Assert.True(await api.Redis.IsDefeatedAsync(encounterId, hero));
        Assert.Equal("downed", (await api.Redis.GetStatusAsync(encounterId, hero))?.Status);
        var planning2 = await _client.NextAsync<PlanningPhaseStartedPayload>(Events.PlanningPhaseStarted, p => p.RoundNumber == 2);
        Assert.Equal("downed", planning2.Combatants.Single(c => c.Id == hero).Status);

        // Round 2: Mend (d6 4 + 2) brings the Hero back up; the Ogre defends.
        api.Dice.Script(4);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Ability("mend", hero)), medic);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var round2 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        Assert.Equal([hero], round2.RevivedCombatantIds);
        var heal = Assert.Single(round2.Ticks.SelectMany(t => t.Heals ?? []));
        Assert.Equal((medic, hero, 6, true, "standing"), (heal.HealerId, heal.TargetId, heal.Amount, heal.Revived, heal.TargetStatus));
        Assert.Equal(0, api.Dice.Remaining);

        // Real Redis: out of the defeated set, status entry gone, HP stored, Mend on cooldown.
        Assert.False(await api.Redis.IsDefeatedAsync(encounterId, hero));
        Assert.Null(await api.Redis.GetStatusAsync(encounterId, hero));
        Assert.Equal(6, await api.Redis.GetCharacterHpAsync(hero));
        Assert.True((await api.Redis.GetCooldownsAsync(encounterId, medic)).ContainsKey("mend"));

        var planning3 = await _client.NextAsync<PlanningPhaseStartedPayload>(Events.PlanningPhaseStarted, p => p.RoundNumber == 3);
        var heroDto = planning3.Combatants.Single(c => c.Id == hero);
        Assert.Equal(("standing", false, 6), (heroDto.Status, heroDto.IsDefeated, heroDto.CurrentHp));

        // Round 3: the Hero is accepted as a planner again, and locks in with the Medic.
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Move(10, 9)), hero);
        Assert.Empty(_client.All<ActionRejectedPayload>(Events.ActionRejected));
        Assert.True(await api.Redis.HasPlannedActionAsync(encounterId, hero));

        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        Assert.True(await api.Redis.IsLockedInAsync(encounterId, hero));
        Assert.True(await api.Redis.IsLockedInAsync(encounterId, medic));

        ContractShape.AssertEventsMatch(_client.Events);
    }
}
