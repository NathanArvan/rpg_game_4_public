using RpgGame.Api.Contracts.Hub;
using RpgGame.Domain.Entities;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

// 3B.6 PR 2: prepared reactions (Brace, Overwatch) through the Api adapter. Pins the new feed lines ("prepares", the
// reaction's hit, "goes unused"), the watching / unusedReactions fields, the GameEvents and the cooldown persisted for a
// reaction that never fired.
public partial class ResolutionServiceTests
{
    private static CombatProfileValues WithAbility(string abilityId)
    {
        var profile = TestProfiles.Values();
        profile.Abilities = [TestProfiles.Catalog.Abilities.Find(abilityId)!.Copy()];
        return profile;
    }

    [Fact]
    public async Task Prepared_BraceHitsArrivingGoblin_FeedAndWireFields()
    {
        await using var s = new Scene();
        var brakka = s.Player("Brakka", (5, 5), WithAbility("brace"));
        var goblin = s.Monster("Goblin", (8, 5));
        s.Queue(brakka, new PlannedActionEntry("ability", null, null, "brace"));
        s.Move(goblin, 6, 5);

        // Brace: d20 12 hits dodge 10; d8 4 + 2 = 6.
        var r = await s.Run(new ScriptedDice(12, 4));

        var tick = Assert.Single(r.Ticks);
        var watching = Assert.Single(tick.Watching!);
        Assert.Equal((brakka, "Brakka", "brace", "Brace", "Brakka prepares Brace"),
            (watching.CombatantId, watching.CombatantName, watching.AbilityId, watching.AbilityName, watching.FeedText));
        Assert.Empty(tick.AbilityUses!);

        var reaction = Assert.Single(tick.Reactions!);
        Assert.Equal("Brakka's Brace hits Goblin for 6 damage (HP: 20 → 14)", reaction.FeedText);
        Assert.Equal(("prepared", "brace", "Brace", 2), (reaction.Kind, reaction.AbilityId, reaction.AbilityName, reaction.Step));
        Assert.Equal(("melee_attack", "brace", 6), (reaction.Attack!.AttackType, reaction.Attack.AbilityId, reaction.Attack.Damage));
        Assert.Empty(r.UnusedReactions!);

        Assert.Single(s.Db.GameEvents.Where(e => e.EventType == "reaction_prepared"));
        Assert.Single(s.Db.GameEvents.Where(e => e.EventType == "reaction_fired"));
        var write = Assert.Single(s.CooldownWrites);
        Assert.Equal((brakka, 1), (write.Id, write.Cooldowns["brace"]));
    }

    [Fact]
    public async Task Prepared_UnusedOverwatch_IsReported_AndItsCooldownIsStored()
    {
        await using var s = new Scene();
        var kestrel = s.Player("Kestrel", (0, 5), WithAbility("overwatch"));
        s.Monster("Goblin", (15, 5));
        s.Queue(kestrel, new PlannedActionEntry("ability", null, null, "overwatch"));

        var r = await s.Run(new ScriptedDice());

        var unused = Assert.Single(r.UnusedReactions!);
        Assert.Equal((kestrel, "Kestrel", "overwatch", "Overwatch", "Kestrel's Overwatch goes unused"),
            (unused.CombatantId, unused.CombatantName, unused.AbilityId, unused.AbilityName, unused.FeedText));
        Assert.Single(s.Db.GameEvents.Where(e => e.EventType == "reaction_unused"));
        var write = Assert.Single(s.CooldownWrites);
        Assert.Equal((kestrel, 1), (write.Id, write.Cooldowns["overwatch"]));
        Assert.Equal(1, r.UpdatedCombatants.Single(c => c.Id == kestrel).Cooldowns!["overwatch"]);
    }

    [Fact]
    public async Task Prepared_AbilityDto_CarriesActivationAndTrigger()
    {
        await using var s = new Scene();
        var kestrel = s.Player("Kestrel", (0, 5), WithAbility("overwatch"));
        s.Monster("Goblin", (15, 5));

        var r = await s.Run(new ScriptedDice());

        var ability = Assert.Single(r.UpdatedCombatants.Single(c => c.Id == kestrel).Abilities!);
        Assert.Equal(("prepared", "trigger"), (ability.Activation, ability.Targeting.Kind));
        Assert.Equal(new AbilityTriggerDto("enemy_moves_within", 6, null, "Reacts when an enemy moves within 6 cells"), ability.Trigger);
    }
}
