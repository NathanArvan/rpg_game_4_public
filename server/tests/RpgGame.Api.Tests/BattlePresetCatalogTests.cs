using RpgGame.Api.CombatProfiles;
using RpgGame.Api.QuickBattle;

namespace RpgGame.Api.Tests;

public class BattlePresetCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("presets-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string file, string json) => File.WriteAllText(Path.Combine(_dir, file), json);

    private const string Valid = """
        { "id": "duel", "name": "Duel", "combatants": [
            { "name": "Hero",   "side": "party", "profile": "scout", "hp": 10, "damage": "d6", "at": [0, 0] },
            { "name": "Goblin", "side": "enemy", "profile": "grunt", "at": [0, 1] } ] }
        """;

    [Fact]
    public void ShippedPresets_AllLoad_AndIncludeTheE2EPreset()
    {
        var catalog = BattlePresetCatalog.FromAppDirectory(TestProfiles.Catalog);
        Assert.Contains(catalog.All, p => p.Id == "skirmish");
        Assert.Contains(catalog.All, p => p.Id == "training-dummy");
        Assert.Contains(catalog.All, p => p.Id == "rescue");
        Assert.Contains(catalog.All, p => p.Id == "reactions");
        Assert.Equal("skirmish", catalog.All[0].Id); // default in the menu
    }

    [Fact]
    public void ValidPreset_Loads_EnemiesDefaultToAi()
    {
        Write("duel.json", Valid);
        var preset = Assert.Single(new BattlePresetCatalog(_dir, TestProfiles.Catalog).All);
        Assert.False(preset.Combatants.Single(c => c.Name == "Hero").IsAiControlled);
        Assert.True(preset.Combatants.Single(c => c.Name == "Goblin").IsAiControlled);
    }

    [Fact]
    public void Profile_IsCopied_AndOverridesWin()
    {
        Write("duel.json", Valid);
        var preset = Assert.Single(new BattlePresetCatalog(_dir, TestProfiles.Catalog).All);

        // Goblin: the grunt profile unchanged.
        var goblin = preset.Combatants.Single(c => c.Name == "Goblin").Profile;
        var grunt = TestProfiles.Catalog.Find("grunt")!;
        Assert.Equal(("grunt", "Grunt", grunt.MaxHp, grunt.MovementRange, grunt.Dodge, grunt.BaseDamage),
            (goblin.ProfileId, goblin.ProfileName, goblin.MaxHp, goblin.MovementRange, goblin.Dodge, goblin.BaseDamage));

        // Hero: the scout profile with hp and damage overridden; everything else from the scout.
        var hero = preset.Combatants.Single(c => c.Name == "Hero").Profile;
        var scout = TestProfiles.Catalog.Find("scout")!;
        Assert.Equal(("scout", 10, "d6", scout.MovementRange, scout.Speed, scout.RangedAccuracy),
            (hero.ProfileId, hero.MaxHp, hero.BaseDamage, hero.MovementRange, hero.Speed, hero.RangedAccuracy));
    }

    [Fact]
    public void AllOverrideFields_Apply()
    {
        Write("o.json", """
            { "id": "o", "name": "O", "combatants": [
                { "name": "A", "side": "party", "profile": "bruiser", "hp": 3, "damage": "d12", "speed": 1,
                  "move": 2, "meleeAccuracy": 5, "rangedAccuracy": -4, "dodge": 17, "ap": 7, "conditionResist": 40, "at": [0, 0] },
                { "name": "B", "side": "enemy", "profile": "grunt", "at": [0, 1] } ] }
            """);
        var a = new BattlePresetCatalog(_dir, TestProfiles.Catalog).All[0].Combatants[0].Profile;
        Assert.Equal((3, "d12", 1, 2, 5, -4, 17, (int?)7, (int?)40),
            (a.MaxHp, a.BaseDamage, a.Speed, a.MovementRange, a.MeleeAccuracy, a.RangedAccuracy, a.Dodge, a.MaxAp, a.ConditionResist));
    }

    [Fact]
    public void OpportunityAttacks_Override_IsStoredOnlyWhenFalse()
    {
        // 3B.6: unset (or true) leaves the stored value null, which reads as "makes opportunity attacks".
        Write("oa.json", """
            { "id": "oa", "name": "OA", "combatants": [
                { "name": "Hero",  "side": "party", "profile": "scout", "at": [0, 0] },
                { "name": "Dummy", "side": "enemy", "profile": "grunt", "opportunityAttacks": false, "at": [0, 1] },
                { "name": "Mook",  "side": "enemy", "profile": "grunt", "opportunityAttacks": true, "at": [0, 2] } ] }
            """);
        var combatants = new BattlePresetCatalog(_dir, TestProfiles.Catalog).All[0].Combatants;
        Assert.Equal([null, false, null], combatants.Select(c => c.Profile.OpportunityAttacks));
        Assert.Equal([true, false, true], combatants.Select(c => CombatProfileMapping.ToRules(c.Profile).OpportunityAttacks));
    }

    [Fact]
    public void ShippedReactions_PutsEachHeroNextToExactlyOneEnemy()
    {
        // The first round is the choice between stepping away (an opportunity attack) and Disengaging.
        var preset = BattlePresetCatalog.FromAppDirectory(TestProfiles.Catalog).All.Single(p => p.Id == "reactions");
        var enemies = preset.Combatants.Where(c => c.Side == "enemy").ToList();
        foreach (var hero in preset.Combatants.Where(c => c.Side == "party"))
            Assert.Single(enemies, e => Math.Max(Math.Abs(e.X - hero.X), Math.Abs(e.Y - hero.Y)) == 1);
    }

    [Fact]
    public void ShippedTrainingDummy_MakesNoOpportunityAttacks()
    {
        var dummy = BattlePresetCatalog.FromAppDirectory(TestProfiles.Catalog).All
            .Single(p => p.Id == "training-dummy").Combatants.Single(c => c.Name == "Dummy");
        Assert.False(CombatProfileMapping.ToRules(dummy.Profile).OpportunityAttacks);
    }

    [Fact]
    public void Downs_OverridesTheSideDefault()
    {
        // 3B.5: unset means by side (party goes down, enemies die); "downs" overrides per combatant.
        Write("d.json", """
            { "id": "d", "name": "D", "combatants": [
                { "name": "Hero", "side": "party", "profile": "scout", "at": [0, 0] },
                { "name": "Boss", "side": "enemy", "profile": "brute", "downs": true, "at": [0, 1] },
                { "name": "Mook", "side": "enemy", "profile": "grunt", "at": [0, 2] } ] }
            """);
        var combatants = new BattlePresetCatalog(_dir, TestProfiles.Catalog).All[0].Combatants;
        Assert.Equal([null, true, null], combatants.Select(c => c.Profile.CanBeDowned));
        Assert.True(CombatProfileMapping.CanBeDowned(combatants[0].Profile, "player"));
        Assert.True(CombatProfileMapping.CanBeDowned(combatants[1].Profile, "monster"));
        Assert.False(CombatProfileMapping.CanBeDowned(combatants[2].Profile, "monster"));
    }

    [Theory]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "profile": "scout", "at": [0, 0] } ] }""", "at least one enemy")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "profile": "wizard", "at": [0, 0] }, { "name": "B", "side": "enemy", "profile": "grunt", "at": [1, 0] } ] }""", "'profile'")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "at": [0, 0] }, { "name": "B", "side": "enemy", "profile": "grunt", "at": [1, 0] } ] }""", "'profile'")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "profile": "scout", "hp": 0, "at": [0, 0] }, { "name": "B", "side": "enemy", "profile": "grunt", "at": [1, 0] } ] }""", "'hp'")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "profile": "scout", "move": 0, "at": [0, 0] }, { "name": "B", "side": "enemy", "profile": "grunt", "at": [1, 0] } ] }""", "'move'")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "profile": "scout", "hp": 5, "damage": "d6", "at": [0, 0] }, { "name": "B", "side": "enemy", "profile": "grunt", "hp": 5, "damage": "d6", "at": [0, 0] } ] }""", "already taken")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "profile": "scout", "hp": 5, "damage": "d6", "at": [0, 20] }, { "name": "B", "side": "enemy", "profile": "grunt", "hp": 5, "damage": "d6", "at": [1, 0] } ] }""", "outside the grid")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "profile": "scout", "hp": 5, "damage": "2d6", "at": [0, 0] }, { "name": "B", "side": "enemy", "profile": "grunt", "hp": 5, "damage": "d6", "at": [1, 0] } ] }""", "'damage'")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "ally", "hp": 5, "damage": "d6", "at": [0, 0] } ] }""", "'side'")]
    [InlineData("""{ "name": "X", "combatants": [] }""", "'id'")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "profile": "scout", "ap": 0, "at": [0, 0] }, { "name": "B", "side": "enemy", "profile": "grunt", "at": [1, 0] } ] }""", "'ap'")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "profile": "scout", "conditionResist": 150, "at": [0, 0] }, { "name": "B", "side": "enemy", "profile": "grunt", "at": [1, 0] } ] }""", "'conditionResist'")]
    [InlineData("""{ not json""", "invalid JSON")]
    public void BrokenPreset_FailsWithTheFileName(string json, string expected)
    {
        Write("broken.json", json);
        var ex = Assert.Throws<InvalidDataException>(() => new BattlePresetCatalog(_dir, TestProfiles.Catalog));
        Assert.Contains("broken.json", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        Write("a.json", Valid);
        Write("b.json", Valid);
        var ex = Assert.Throws<InvalidDataException>(() => new BattlePresetCatalog(_dir, TestProfiles.Catalog));
        Assert.Contains("'duel'", ex.Message);
    }
}
