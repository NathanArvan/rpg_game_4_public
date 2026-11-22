using RpgGame.Api.Contracts.Hub;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

// Abilities (3B.3) through ResolutionService: feed text, payload fields and cooldown persistence.
// Uses the characterization Scene harness; existing basic-attack strings are pinned there.
public partial class ResolutionServiceTests
{
    private static PlannedActionEntry Ability(string abilityId, string? target) => new("ability", null, target, abilityId);

    [Fact]
    public async Task Ability_PowerStrikeHit_FeedNamesTheAbility_AndCooldownIsStored()
    {
        await using var s = new Scene();
        var bruiser = s.Player("Bruiser", (0, 0), TestProfiles.Catalog.Find("bruiser")!.Snapshot());
        var goblin = s.Monster("Goblin", (1, 0));
        s.Queue(bruiser, Ability("power_strike", goblin));

        // Bruiser: melee accuracy +2, d8. 8 + 2 = 10 hits dodge 10; d8 5 + 4 = 9.
        var result = await s.Run(new ScriptedDice(8, 5));

        var tick = Assert.Single(result.Ticks);
        var attack = Assert.Single(tick.Attacks);
        Assert.Equal("Bruiser's Power Strike hits Goblin for 9 damage (HP: 20 → 11)", attack.FeedText);
        Assert.Equal(("power_strike", "melee_attack", 9), (attack.AbilityId, attack.AttackType, attack.Damage));

        var use = Assert.Single(tick.AbilityUses!);
        Assert.Equal(("Bruiser", "Power Strike", (string?)null), (use.CombatantName, use.AbilityName, use.WastedReason));
        Assert.Equal("Bruiser uses Power Strike on Goblin", use.FeedText);

        var write = Assert.Single(s.CooldownWrites);
        Assert.Equal(bruiser, write.Id);
        Assert.Equal(new Dictionary<string, int> { ["power_strike"] = 2 }, write.Cooldowns);
        var next = result.UpdatedCombatants.Single(c => c.Id == bruiser);
        Assert.Equal(2, next.Cooldowns!["power_strike"]);
        Assert.Equal(["power_strike", "cleave"], next.Abilities!.Select(a => a.Id));

        var queued = Assert.Single(Assert.Single(result.Queues, q => q.CombatantId == bruiser).Actions);
        Assert.Equal(("ability", 3, "power_strike"), (queued.Type, queued.ApCost, queued.AbilityId));
    }

    [Fact]
    public async Task Ability_OutOfRange_FeedSaysSo()
    {
        await using var s = new Scene();
        var bruiser = s.Player("Bruiser", (0, 0), TestProfiles.Catalog.Find("bruiser")!.Snapshot());
        var goblin = s.Monster("Goblin", (3, 0));
        s.Queue(bruiser, Ability("power_strike", goblin));

        var result = await s.Run(new ScriptedDice());

        var attack = SingleAttack(result);
        Assert.Equal("Bruiser's Power Strike on Goblin fails — target out of range", attack.FeedText);
        Assert.Equal("out_of_range", attack.WastedReason);
        Assert.Single(s.CooldownWrites); // still starts its cooldown
    }

    [Fact]
    public async Task Ability_OnCooldown_IsRefused_WithAFeedLine()
    {
        await using var s = new Scene();
        var bruiser = s.Player("Bruiser", (0, 0), TestProfiles.Catalog.Find("bruiser")!.Snapshot());
        var goblin = s.Monster("Goblin", (1, 0));
        s.Cooldowns[bruiser] = new() { ["power_strike"] = 1 };
        s.Queue(bruiser, Ability("power_strike", goblin));

        var result = await s.Run(new ScriptedDice());

        var tick = Assert.Single(result.Ticks);
        Assert.Empty(tick.Attacks);
        var use = Assert.Single(tick.AbilityUses!);
        Assert.Equal(("on_cooldown", "Bruiser can't use Power Strike — still on cooldown"), (use.WastedReason, use.FeedText));
        var write = Assert.Single(s.CooldownWrites); // 1 → 0: cleared
        Assert.Equal(bruiser, write.Id);
        Assert.Empty(write.Cooldowns);
    }

    [Fact]
    public async Task Cleave_HitsEachAdjacentEnemy_OneLinePerTarget()
    {
        await using var s = new Scene();
        var bruiser = s.Player("Bruiser", (5, 5), TestProfiles.Catalog.Find("bruiser")!.Snapshot());
        var a = s.Monster("Goblin A", (4, 5));
        var b = s.Monster("Goblin B", (6, 6));
        s.Queue(bruiser, Ability("cleave", null));

        // Targets in distance order, then by id; Bruiser +2 melee. One hits (d8 4), one misses.
        var firstIsA = string.CompareOrdinal(a, b) < 0;
        var result = await s.Run(new ScriptedDice(15, 4, 3));

        var tick = Assert.Single(result.Ticks);
        var use = Assert.Single(tick.AbilityUses!);
        Assert.Equal(firstIsA ? [a, b] : [b, a], use.TargetIds);
        var (first, second) = firstIsA ? ("Goblin A", "Goblin B") : ("Goblin B", "Goblin A");
        Assert.Equal(
            [$"Bruiser's Cleave hits {first} for 4 damage (HP: 20 → 16)", $"Bruiser's Cleave misses {second}"],
            tick.Attacks.Select(x => x.FeedText));
        Assert.All(tick.Attacks, x => Assert.Equal("cleave", x.AbilityId));
    }

    [Fact]
    public async Task Cleave_WithNobodyInReach_SaysSo_AndStillStartsTheCooldown()
    {
        await using var s = new Scene();
        var bruiser = s.Player("Bruiser", (5, 5), TestProfiles.Catalog.Find("bruiser")!.Snapshot());
        s.Monster("Goblin", (9, 9));
        s.Queue(bruiser, Ability("cleave", null));

        var result = await s.Run(new ScriptedDice());

        var use = Assert.Single(Assert.Single(result.Ticks).AbilityUses!);
        Assert.Equal(("no_targets", "Bruiser uses Cleave — no targets in range"), (use.WastedReason, use.FeedText));
        Assert.Equal(2, Assert.Single(s.CooldownWrites).Cooldowns["cleave"]);
    }

    [Fact]
    public async Task TwinShot_TwoChosenTargets_EachRolls()
    {
        await using var s = new Scene();
        var scout = s.Player("Scout", (0, 0), TestProfiles.Catalog.Find("scout")!.Snapshot());
        var a = s.Monster("Goblin A", (4, 0));
        var b = s.Monster("Goblin B", (0, 5));
        s.Queue(scout, new PlannedActionEntry("ability", null, null, "twin_shot", [b, a]));

        // Scout: ranged +2, d4. Goblin B first (plan order of targets): hit for 3; Goblin A: hit for 1.
        var result = await s.Run(new ScriptedDice(10, 3, 12, 1));

        var tick = Assert.Single(result.Ticks);
        Assert.Equal([b, a], tick.Attacks.Select(x => x.TargetId));
        Assert.Equal([3, 1], tick.Attacks.Select(x => x.Damage));
        var queued = Assert.Single(Assert.Single(result.Queues, q => q.CombatantId == scout).Actions);
        Assert.Equal([b, a], queued.TargetCombatantIds!);
    }

    [Fact]
    public async Task Mend_RevivesADownedAlly_ClearsRedis_AndSaysSo()
    {
        await using var s = new Scene();
        var hero = s.Player("Brakka", (5, 5));
        var mira = s.Player("Mira", (5, 3), TestProfiles.Catalog.Find("skirmisher")!.Snapshot());
        s.Monster("Goblin", (15, 15));
        s.StartDowned(hero, 1);
        s.Queue(mira, Ability("mend", hero));

        var result = await s.Run(new ScriptedDice(4)); // d6 4 + 2 = 6

        var heal = Assert.Single(Assert.Single(result.Ticks).Heals!);
        Assert.Equal("Mira's Mend heals Brakka for 6 (HP: 0 → 6) — back on their feet!", heal.FeedText);
        Assert.Equal((6, 6, true, "standing", (string?)null), (heal.Amount, heal.TargetNewHp, heal.Revived, heal.TargetStatus, heal.WastedReason));
        Assert.Equal([hero], result.RevivedCombatantIds!);
        Assert.Empty(result.DefeatedCombatantIds);
        Assert.Empty(result.EndOfRound); // no bleedout tick for someone back up
        var next = result.UpdatedCombatants.Single(c => c.Id == hero);
        Assert.False(next.IsDefeated);
        Assert.Equal(6, next.CurrentHp);

        // Out of the defeated set and the status hash (after the GameEvents are written), HP stored.
        Assert.Contains(s.CallLog, l => l.StartsWith($"Revive {hero} events=") && !l.EndsWith("events=0"));
        Assert.DoesNotContain(s.CallLog, l => l.StartsWith($"SetStatus {hero}"));
        s.Redis.Verify(r => r.SetHpAsync(s.HpKeys[hero], 6), Moq.Times.Once);
        Assert.Contains(s.Db.GameEvents, e => e.EventType == "heal_revived");
    }

    [Fact]
    public async Task Mend_AndAFinishingBlowInTheSameTick_TheBlowWins()
    {
        await using var s = new Scene();
        var hero = s.Player("Brakka", (5, 5));
        var goblin = s.Monster("Goblin", (5, 6));
        var mira = s.Player("Mira", (5, 3), TestProfiles.Catalog.Find("skirmisher")!.Snapshot());
        s.StartDowned(hero, 2);
        s.Melee(goblin, hero);
        s.Queue(mira, Ability("mend", hero));

        var result = await s.Run(new ScriptedDice(14, 4)); // finishing blow hits; d6 4

        var heal = Assert.Single(Assert.Single(result.Ticks).Heals!);
        Assert.Equal("Mira's Mend on Brakka comes too late", heal.FeedText);
        Assert.Empty(result.RevivedCombatantIds!);
        s.Redis.Verify(r => r.ReviveAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Never);
    }

    [Fact]
    public async Task Mend_OnAHurtAlly_NetsWithDamage()
    {
        await using var s = new Scene();
        var hero = s.Player("Brakka", (5, 5), hp: 10);
        var goblin = s.Monster("Goblin", (5, 6));
        var mira = s.Player("Mira", (5, 3), TestProfiles.Catalog.Find("skirmisher")!.Snapshot());
        s.Melee(goblin, hero);
        s.Queue(mira, Ability("mend", hero));

        var result = await s.Run(new ScriptedDice(15, 5, 1)); // goblin hits for 5; d6 1 + 2 = 3

        var tick = Assert.Single(result.Ticks);
        Assert.Equal("Mira's Mend heals Brakka for 3 (HP: 10 → 8)", Assert.Single(tick.Heals!).FeedText);
        Assert.Equal("Goblin strikes Brakka for 5 damage (HP: 10 → 8)", Assert.Single(tick.Attacks).FeedText);
    }

    [Fact]
    public async Task NoAbilities_WritesNoCooldowns()
    {
        await using var s = new Scene();
        var hero = s.Player("Hero", (0, 0));
        var goblin = s.Monster("Goblin", (1, 0));
        s.Melee(hero, goblin);

        await s.Run(new ScriptedDice(15, 3));

        Assert.Empty(s.CooldownWrites);
    }
}
