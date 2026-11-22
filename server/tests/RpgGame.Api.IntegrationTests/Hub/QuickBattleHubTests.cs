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

        // Round 1: Hero hits (d20 15 + 1 ≥ dodge 10) for 4; the manual Goblin defends.
        api.Dice.Script(15, 4);
        await _client.InvokeAsync(Methods.PlanAction, battle.JoinCode, Queue(Melee(goblin)), hero);
        await _client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await _client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);

        var round1 = await _client.NextAsync<ResolutionPayload>(Events.ResolutionCompleted);
        var attack = Assert.Single(round1.Ticks.SelectMany(t => t.Attacks));
        Assert.Equal((hero, goblin, true, 4, 6), (attack.AttackerId, attack.TargetId, attack.Hit, attack.Damage, attack.TargetNewHp));
        Assert.Equal(new AttackRollDto(15, 1, 10), attack.Roll);
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
