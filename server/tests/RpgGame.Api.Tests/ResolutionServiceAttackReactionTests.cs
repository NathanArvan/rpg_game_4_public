using RpgGame.Api.Contracts.Hub;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

// 3B.6 PR 3: attack triggers (Parry, Intercept) through the Api adapter. Pins the feed lines ("parries (+4 Dodge)",
// "intercepts … attack on …"), the attack's reactionDodge / interceptedFromId on the wire and the reaction_fired event.
public partial class ResolutionServiceTests
{
    [Fact]
    public async Task Attack_ParryTurnsAHitIntoAMiss_FeedAndRoll()
    {
        await using var s = new Scene();
        var kestrel = s.Player("Kestrel", (5, 5), WithAbility("parry"));
        var goblin = s.Monster("Goblin", (6, 5));
        s.Queue(kestrel, new PlannedActionEntry("ability", null, null, "parry"));
        s.Melee(goblin, kestrel);

        // 11 would hit Dodge 10; Parry makes it 14.
        var r = await s.Run(new ScriptedDice(11));

        var tick = Assert.Single(r.Ticks);
        var reaction = Assert.Single(tick.Reactions!);
        Assert.Equal(("Kestrel parries (+4 Dodge)", "prepared", "parry", 0, (AttackResult?)null),
            (reaction.FeedText, reaction.Kind, reaction.AbilityId, reaction.Step, reaction.Attack));
        Assert.Equal((kestrel, goblin), (reaction.ReactorId, reaction.TriggerId));
        var attack = Assert.Single(tick.Attacks);
        Assert.Equal("Goblin swings at Kestrel and misses", attack.FeedText);
        Assert.Equal(new AttackRollDto(11, 0, 14, 0, 0, 4), attack.Roll);
        Assert.Empty(r.UnusedReactions!);
        Assert.Single(s.Db.GameEvents.Where(e => e.EventType == "reaction_fired"));
    }

    [Fact]
    public async Task Attack_InterceptTakesTheHitMeantForAnAlly_FeedAndWireFields()
    {
        await using var s = new Scene();
        var brakka = s.Player("Brakka", (5, 5), WithAbility("intercept"));
        var mira = s.Player("Mira", (6, 5));
        var goblin = s.Monster("Goblin", (7, 5));
        s.Queue(brakka, new PlannedActionEntry("ability", null, null, "intercept"));
        s.Melee(goblin, mira);

        var r = await s.Run(new ScriptedDice(12, 5));

        var tick = Assert.Single(r.Ticks);
        var reaction = Assert.Single(tick.Reactions!);
        Assert.Equal("Brakka intercepts Goblin's attack on Mira", reaction.FeedText);
        var attack = Assert.Single(tick.Attacks);
        Assert.Equal((brakka, mira, "Goblin strikes Brakka for 5 damage (HP: 20 → 15)"),
            (attack.TargetId, attack.InterceptedFromId, attack.FeedText));
        Assert.Equal(15, r.UpdatedCombatants.Single(c => c.Id == brakka).CurrentHp);
        Assert.Equal(20, r.UpdatedCombatants.Single(c => c.Id == mira).CurrentHp);
    }
}
