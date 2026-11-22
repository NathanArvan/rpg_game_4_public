using RpgGame.Api.Abilities;
using RpgGame.Api.CombatProfiles;
using RpgGame.Api.QuickBattle;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

/// <summary>The ability catalog (3B.3): loading, validation, loadouts and mapping to the rules engine.</summary>
public class AbilityCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("abilities-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string file, string json)
    {
        var path = Path.Combine(_dir, file);
        File.WriteAllText(path, json);
        return path;
    }

    private const string Strike = """
        { "id": "strike", "name": "Strike", "ap": 3, "cooldown": 2,
          "target": { "kind": "combatant", "filter": "enemy", "range": 1 },
          "effects": [ { "kind": "attack", "roll": "melee", "damage": { "bonus": 2 } } ] }
        """;

    [Fact]
    public void ShippedAbilities_Load_AndMapToTheRulesEngine()
    {
        var catalog = TestProfiles.Catalog.Abilities;
        var strike = catalog.Find("power_strike")!;
        Assert.Equal(("Power Strike", 3, 2), (strike.Name, strike.ApCost, strike.Cooldown));

        var rules = AbilityMapping.ToRules(strike);
        Assert.Equal(new Targeting(TargetKind.Combatant, TargetFilter.Enemy, 1), rules.Targeting);
        Assert.Equal(new AttackEffect(AttackKind.Melee, new DamageSpec(null, 4)), Assert.Single(rules.Effects));

        var firebolt = AbilityMapping.ToRules(catalog.Find("firebolt")!);
        Assert.Equal(new DamageSpec(DamageDie.D8, 1), ((AttackEffect)firebolt.Effects[0]).Damage);
        Assert.Contains("spell", firebolt.Tags);
    }

    [Fact]
    public void ShippedProfiles_GiveBruiserAndBrutePowerStrike()
    {
        foreach (var id in new[] { "bruiser", "brute" })
            Assert.Equal(["power_strike"], TestProfiles.Catalog.Find(id)!.Abilities.Select(a => a.Id));
        Assert.Empty(TestProfiles.Catalog.Find("grunt")!.Abilities);
    }

    [Fact]
    public void OptionalFields_Default()
    {
        var a = Assert.Single(AbilityCatalog.Load(Write("abilities.json", $$"""{ "abilities": [ {{Strike}} ] }""")));
        Assert.Equal(("", 0, false), (a.Description, a.Effects[0].AccuracyBonus, a.Target.IncludeSelf));
        Assert.Equal(("weapon", 2, 1), (a.Effects[0].Damage!.Die, a.Effects[0].Damage!.Bonus, a.Effects[0].Damage!.Multiplier));
        Assert.Empty(a.Tags);
    }

    [Theory]
    [InlineData("""{ "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'id'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 0, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'ap'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "cooldown": -1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'cooldown'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'target'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "auto", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "isn't supported yet")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "foes", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'filter'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 0 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'range'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [] }""", "'effects'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "fly" } ] }""", "unknown effect kind 'fly'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "kick" } ] }""", "'roll'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee", "damage": { "die": "2d6" } } ] }""", "'die'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee", "damage": { "multiplier": 0 } } ] }""", "'multiplier'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "charges": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'charges'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "activation": "prepared", "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'activation'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ], "resonanceScaling": { "lava": {} } }""", "'lava'")]
    public void BrokenAbility_FailsWithTheFileAndAbility(string ability, string expected)
    {
        var path = Write("abilities.json", $$"""{ "abilities": [ {{ability}} ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => AbilityCatalog.Load(path));
        Assert.Contains("abilities.json", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var path = Write("abilities.json", $$"""{ "abilities": [ {{Strike}}, {{Strike}} ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => AbilityCatalog.FromFile(path));
        Assert.Contains("'strike'", ex.Message);
    }

    [Fact]
    public void ProfileWithAnUnknownAbility_StopsStartup()
    {
        var abilities = AbilityCatalog.FromFile(Write("abilities.json", $$"""{ "abilities": [ {{Strike}} ] }"""));
        var profiles = Write("profiles.json", """
            { "profiles": [
                { "id": "skirmisher", "name": "S", "hp": 1, "move": 1, "speed": 1, "dodge": 1, "damage": "d4", "abilities": ["strike", "fly"] },
                { "id": "grunt", "name": "G", "hp": 1, "move": 1, "speed": 1, "dodge": 1, "damage": "d4" } ] }
            """);
        var ex = Assert.Throws<InvalidDataException>(() => CombatProfileCatalog.FromFile(profiles, abilities));
        Assert.Contains("skirmisher", ex.Message);
        Assert.Contains("'fly'", ex.Message);
    }

    [Fact]
    public void PresetAbilities_ReplaceTheProfileLoadout()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_dir, "presets")).FullName;
        File.WriteAllText(Path.Combine(dir, "duel.json"), """
            { "id": "duel", "name": "Duel", "combatants": [
                { "name": "Mage",  "side": "party", "profile": "scout",   "abilities": ["firebolt"], "at": [0, 0] },
                { "name": "Plain", "side": "party", "profile": "bruiser", "abilities": [], "at": [2, 0] },
                { "name": "Ogre",  "side": "enemy", "profile": "brute",   "at": [0, 1] } ] }
            """);
        var combatants = Assert.Single(new BattlePresetCatalog(dir, TestProfiles.Catalog).All).Combatants;
        Assert.Equal(["firebolt"], combatants.Single(c => c.Name == "Mage").Profile.Abilities!.Select(a => a.Id));
        Assert.Empty(combatants.Single(c => c.Name == "Plain").Profile.Abilities!);
        Assert.Equal(["power_strike"], combatants.Single(c => c.Name == "Ogre").Profile.Abilities!.Select(a => a.Id));
    }

    [Fact]
    public void PresetWithAnUnknownAbility_FailsWithTheFileName()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_dir, "presets")).FullName;
        File.WriteAllText(Path.Combine(dir, "duel.json"), """
            { "id": "duel", "name": "Duel", "combatants": [
                { "name": "Mage", "side": "party", "profile": "scout", "abilities": ["meteor"], "at": [0, 0] },
                { "name": "Ogre", "side": "enemy", "profile": "brute", "at": [0, 1] } ] }
            """);
        var ex = Assert.Throws<InvalidDataException>(() => new BattlePresetCatalog(dir, TestProfiles.Catalog));
        Assert.Contains("duel.json", ex.Message);
        Assert.Contains("'meteor'", ex.Message);
    }

    [Fact]
    public void AbilitiesPreset_KeepsAGoblinNextToTheBruiser_ForTheE2ETest()
    {
        var preset = BattlePresetCatalog.FromAppDirectory(TestProfiles.Catalog).Find("abilities")!;
        var bruiser = preset.Combatants.Single(c => c.Name == "Bruiser");
        Assert.Contains(preset.Combatants, c => c.Side == "enemy"
            && Math.Max(Math.Abs(c.X - bruiser.X), Math.Abs(c.Y - bruiser.Y)) == 1);
        Assert.Equal(["power_strike"], bruiser.Profile.Abilities!.Select(a => a.Id));
    }
}
