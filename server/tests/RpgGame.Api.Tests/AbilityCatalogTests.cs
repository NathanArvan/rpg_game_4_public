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
    public void ShippedProfiles_HaveTheirLoadouts()
    {
        string[] Ids(string profile) => TestProfiles.Catalog.Find(profile)!.Abilities.Select(a => a.Id).ToArray();
        Assert.Equal(["power_strike", "cleave"], Ids("bruiser"));
        Assert.Equal(["power_strike", "charge"], Ids("brute"));
        Assert.Equal(["mend", "dash", "charge"], Ids("skirmisher"));
        Assert.Equal(["twin_shot", "dash"], Ids("scout"));
        Assert.Empty(Ids("grunt"));
    }

    [Fact]
    public void ShippedMultiTargetAndHealAbilities_MapToTheRulesEngine()
    {
        var catalog = AbilityCatalog.FromAppDirectory();
        var cleave = AbilityMapping.ToRules(catalog.Find("cleave")!);
        Assert.Equal(new Targeting(TargetKind.Auto, TargetFilter.Enemy, 1), cleave.Targeting);
        var twin = AbilityMapping.ToRules(catalog.Find("twin_shot")!);
        Assert.Equal(new Targeting(TargetKind.Combatants, TargetFilter.Enemy, 8, Count: 2), twin.Targeting);
        var mend = AbilityMapping.ToRules(catalog.Find("mend")!);
        Assert.Equal(new Targeting(TargetKind.Combatant, TargetFilter.Ally, 3, IncludeSelf: true), mend.Targeting);
        Assert.Equal(new HealEffect(DamageDie.D6, 2, CanRevive: true), Assert.Single(mend.Effects));
    }

    [Fact]
    public void ShippedMovementAbilities_MapToTheRulesEngine()
    {
        var catalog = AbilityCatalog.FromAppDirectory();
        var dash = AbilityMapping.ToRules(catalog.Find("dash")!);
        Assert.Equal(new Targeting(TargetKind.Self, TargetFilter.Ally, 0), dash.Targeting);
        Assert.Equal(new MoveEffect(MoveTo.Cell, 3, UsesMovementBudget: false), Assert.Single(dash.Effects));
        Assert.True(dash.NeedsCell);

        var charge = AbilityMapping.ToRules(catalog.Find("charge")!);
        Assert.Equal(new Targeting(TargetKind.Combatant, TargetFilter.Enemy, 6), charge.Targeting);
        Assert.Equal(new MoveEffect(MoveTo.Target, 6, UsesMovementBudget: true), charge.Effects[0]);
        Assert.Equal(new DamageSpec(BonusIfMoved: new MovedBonus(2, 3)), ((AttackEffect)charge.Effects[1]).Damage);
    }

    [Fact]
    public void HealWithOnlyABonus_HasNoDie()
    {
        var a = Assert.Single(AbilityCatalog.Load(Write("abilities.json", """
            { "abilities": [ { "id": "salve", "name": "Salve", "ap": 1, "target": { "kind": "combatant", "filter": "ally", "range": 1 },
              "effects": [ { "kind": "heal", "amount": { "bonus": 3 }, "canRevive": false } ] } ] }
            """)));
        Assert.Equal(new HealEffect(null, 3, CanRevive: false), AbilityMapping.ToRules(a).Effects.Single());
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
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "self" }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "a \"self\" ability can't attack")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "self", "filter": "enemy" }, "effects": [ { "kind": "move", "to": "cell", "maxCells": 3 } ] }""", "doesn't take a target 'filter'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "self" }, "effects": [ { "kind": "move", "to": "home", "maxCells": 3 } ] }""", "'to'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "self" }, "effects": [ { "kind": "move", "to": "cell", "maxCells": 0 } ] }""", "'maxCells'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "self" }, "effects": [ { "kind": "move", "to": "cell", "maxCells": 3, "path": "teleport" } ] }""", "'path'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "self" }, "effects": [ { "kind": "move", "to": "cell", "maxCells": 3 }, { "kind": "move", "to": "cell", "maxCells": 3 } ] }""", "only once")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 4 }, "effects": [ { "kind": "move", "to": "cell", "maxCells": 3 } ] }""", "needs a \"self\" target kind")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "self" }, "effects": [ { "kind": "move", "to": "target", "maxCells": 3 } ] }""", "needs a \"combatant\" target kind")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee", "damage": { "bonusIfMoved": { "minCells": 2, "bonus": 3 } } } ] }""", "needs a \"move\" effect")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 4 }, "effects": [ { "kind": "move", "to": "target", "maxCells": 3 }, { "kind": "attack", "roll": "melee", "damage": { "bonusIfMoved": { "minCells": 0, "bonus": 3 } } } ] }""", "'bonusIfMoved'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "cell", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "isn't supported yet")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatants", "filter": "enemy", "range": 4 }, "effects": [ { "kind": "attack", "roll": "ranged" } ] }""", "'count' between 1 and")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "auto", "filter": "enemy", "range": 1, "count": 0 }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'count' must be at least 1")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1, "includeSelf": true }, "effects": [ { "kind": "attack", "roll": "melee" } ] }""", "'includeSelf'")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "heal", "amount": { "bonus": 2 } } ] }""", "a heal can't target enemies")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "ally", "range": 1 }, "effects": [ { "kind": "heal" } ] }""", "a heal needs an amount")]
    [InlineData("""{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "ally", "range": 1 }, "effects": [ { "kind": "heal", "amount": { "die": "lots" } } ] }""", "amount 'die'")]
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
        Assert.Equal(["power_strike", "charge"], combatants.Single(c => c.Name == "Ogre").Profile.Abilities!.Select(a => a.Id));
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
        Assert.Equal(["power_strike", "cleave"], bruiser.Profile.Abilities!.Select(a => a.Id));
    }

    [Fact]
    public void AbilitiesPreset_SetsUpTheCleaveAndMendE2ETests()
    {
        var preset = BattlePresetCatalog.FromAppDirectory(TestProfiles.Catalog).Find("abilities")!;
        static int Distance(PresetCombatant a, PresetCombatant b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        var bruiser = preset.Combatants.Single(c => c.Name == "Bruiser");
        Assert.True(preset.Combatants.Count(c => c.Side == "enemy" && Distance(c, bruiser) == 1) >= 2, "two goblins next to the Bruiser, for Cleave");

        var skirmisher = preset.Combatants.Single(c => c.Name == "Skirmisher");
        var scout = preset.Combatants.Single(c => c.Name == "Scout");
        Assert.True(scout.StartHp < scout.Profile.MaxHp, "the Scout starts hurt");
        Assert.True(Distance(scout, skirmisher) <= 3, "the hurt Scout is within Mend's range of the Skirmisher");
        Assert.Contains(preset.Combatants, c => c.Side == "enemy" && c.Profile.Abilities!.Any(a => a.Id == "mend"));
    }

    [Fact]
    public void PresetStartHp_MustFitMaxHp()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_dir, "presets")).FullName;
        File.WriteAllText(Path.Combine(dir, "hurt.json"), """
            { "id": "hurt", "name": "Hurt", "combatants": [
                { "name": "Scout", "side": "party", "profile": "scout", "startHp": 99, "at": [0, 0] },
                { "name": "Ogre",  "side": "enemy", "profile": "brute", "at": [0, 1] } ] }
            """);
        var ex = Assert.Throws<InvalidDataException>(() => new BattlePresetCatalog(dir, TestProfiles.Catalog));
        Assert.Contains("'startHp'", ex.Message);
    }
}
