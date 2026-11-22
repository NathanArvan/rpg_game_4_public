using RpgGame.Api.Contracts.Hub;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

// Abilities (3B.3) through ResolutionService: feed text, payload fields and cooldown persistence.
// Uses the characterization Scene harness; existing basic-attack strings are pinned there.
public partial class ResolutionServiceTests
{
    private static PlannedActionEntry Ability(string abilityId, string target) => new("ability", null, target, abilityId);

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
        Assert.Equal(["power_strike"], next.Abilities!.Select(a => a.Id));

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
