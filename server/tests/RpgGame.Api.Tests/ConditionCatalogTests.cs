using RpgGame.Api.Abilities;
using RpgGame.Api.Conditions;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

/// <summary>The condition catalog (3B.4) and "apply_condition" effects in the ability catalog.</summary>
public class ConditionCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("conditions-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string file, string json)
    {
        var path = Path.Combine(_dir, file);
        File.WriteAllText(path, json);
        return path;
    }

    private const string Marked = """
        { "id": "marked", "name": "Marked", "harmful": true, "description": "Takes 2 more.",
          "modifiers": { "damageTaken": { "flat": 2 } } }
        """;

    private const string Blessed = """{ "id": "blessed", "name": "Blessed", "harmful": false, "modifiers": { "accuracy": 2 } }""";

    private ConditionCatalog Conditions() => ConditionCatalog.FromFile(Write("conditions.json", $$"""{ "conditions": [ {{Marked}}, {{Blessed}} ] }"""));

    // ----- Shipped file -----

    [Fact]
    public void ShippedConditions_Load_AndMapToTheRulesEngine()
    {
        var catalog = TestProfiles.Catalog.Abilities.Conditions;
        Assert.Equal(["blessed", "dazzled", "exposed", "emboldened", "fortified", "marked", "flame_ward", "burning", "poisoned", "regenerating"],
            catalog.All.Select(c => c.Id));

        var dazzled = ConditionMapping.ToRules(catalog.Find("dazzled")!);
        Assert.Equal(new Modifiers(Accuracy: -3, AttackKind: AttackKindFilter.Ranged), dazzled.Modifiers);
        Assert.True(dazzled.Harmful);

        var ward = ConditionMapping.ToRules(catalog.Find("flame_ward")!);
        Assert.Equal(new DamageTakenModifier(-50, 0, ["flame"]), ward.Modifiers.DamageTaken);
        Assert.False(ward.Harmful);
    }

    [Fact]
    public void ShippedPeriodicConditions_MapToTheRulesEngine()
    {
        var catalog = TestProfiles.Catalog.Abilities.Conditions;
        var burning = ConditionMapping.ToRules(catalog.Find("burning")!);
        Assert.Equal(new PeriodicEffect(PeriodicKind.Damage, DamageDie.D4), burning.Modifiers.Periodic);
        Assert.Equal(["flame"], burning.Tags);
        Assert.True(burning.Harmful);
        Assert.Equal(new PeriodicEffect(PeriodicKind.Damage, null, 2), ConditionMapping.ToRules(catalog.Find("poisoned")!).Modifiers.Periodic);
        Assert.Equal(new PeriodicEffect(PeriodicKind.Heal, DamageDie.D4), ConditionMapping.ToRules(catalog.Find("regenerating")!).Modifiers.Periodic);
    }

    [Fact]
    public void ShippedFireboltAndEnvenom_ApplyTheirConditionsOnHit()
    {
        var abilities = TestProfiles.Catalog.Abilities;
        var firebolt = AbilityMapping.ToRules(abilities.Find("firebolt")!);
        var burn = Assert.Single(firebolt.ConditionEffects);
        Assert.Equal(("burning", 1, true), (burn.Condition.Id, burn.Duration, burn.RequiresHit));
        var envenom = AbilityMapping.ToRules(abilities.Find("envenom")!);
        var poison = Assert.Single(envenom.ConditionEffects);
        Assert.Equal(("poisoned", 2, true), (poison.Condition.Id, poison.Duration, poison.RequiresHit));
    }

    [Fact]
    public void ShippedConditionAbilities_EmbedTheirConditions()
    {
        var abilities = TestProfiles.Catalog.Abilities;
        var mark = AbilityMapping.ToRules(abilities.Find("mark")!);
        Assert.Equal(["marked", "exposed"], mark.ConditionEffects.Select(e => e.Condition.Id));
        Assert.All(mark.ConditionEffects, e => Assert.Equal((1, false), (e.Duration, e.RequiresHit)));

        var cry = AbilityMapping.ToRules(abilities.Find("battle_cry")!);
        Assert.Equal(new Targeting(TargetKind.Auto, TargetFilter.Ally, 3, IncludeSelf: true), cry.Targeting);
        Assert.Equal("emboldened", Assert.Single(cry.ConditionEffects).Condition.Id);

        var stoneskin = abilities.Find("stoneskin")!;
        Assert.Equal("Takes half damage from every hit.", stoneskin.Effects[0].Condition!.Description);
    }

    [Fact]
    public void Guarded_IsBuiltIn_AndMatchesTheRulesEngine()
    {
        Assert.Equal(RulesConfig.Default.GuardedCondition, ConditionMapping.ToRules(ConditionCatalog.Guarded));
        Assert.NotEmpty(ConditionCatalog.Guarded.Description);
    }

    [Fact]
    public void Mapping_RoundTrips_EveryModifier()
    {
        var c = Assert.Single(ConditionCatalog.Load(Write("conditions.json", """
            { "conditions": [ { "id": "all", "name": "All", "harmful": true, "tags": ["Flame"],
              "modifiers": { "accuracy": 1, "dodge": -2, "attackKind": "melee", "damageDealt": 3,
                "damageTaken": { "percent": 50, "flat": -1, "tags": ["ranged"] }, "movement": -2, "ap": -1,
                "periodic": { "kind": "damage", "die": "d4", "bonus": 1 }, "blocks": ["move", "all", "MOVE"] } } ] }
            """)));
        Assert.Equal(["flame"], c.Tags);
        Assert.Equal(["move", "all"], c.Modifiers.Blocks);

        var rules = ConditionMapping.ToRules(c);
        var back = ConditionMapping.ToRules(ConditionMapping.FromRules(rules));
        Assert.Equal(rules, back);
        Assert.Equal(new PeriodicEffect(PeriodicKind.Damage, DamageDie.D4, 1), rules.Modifiers.Periodic);
        Assert.Equal([BlockKind.Move, BlockKind.All], rules.Modifiers.Blocks);
    }

    // ----- Validation -----

    [Theory]
    [InlineData("""{ "name": "X", "harmful": true, "modifiers": { "dodge": -1 } }""", "'id'")]
    [InlineData("""{ "id": "x", "harmful": true, "modifiers": { "dodge": -1 } }""", "'name'")]
    [InlineData("""{ "id": "x", "name": "X", "modifiers": { "dodge": -1 } }""", "'harmful'")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true }""", "'modifiers' is required")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": {} }""", "must change something")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": { "accuracy": 99 } }""", "'accuracy'")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": { "dodge": -1, "attackKind": "thrown" } }""", "'attackKind'")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": { "damageDealt": 1, "attackKind": "melee" } }""", "only applies with 'accuracy' or 'dodge'")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": { "damageTaken": { "percent": 150 } } }""", "'percent'")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": { "damageTaken": {} } }""", "needs a 'percent' or a 'flat'")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": { "periodic": { "kind": "burn", "bonus": 1 } } }""", "periodic 'kind'")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": { "periodic": { "kind": "damage" } } }""", "periodic needs an amount")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": { "periodic": { "kind": "damage", "die": "2d6" } } }""", "periodic 'die'")]
    [InlineData("""{ "id": "x", "name": "X", "harmful": true, "modifiers": { "blocks": ["attack"] } }""", "'blocks'")]
    public void BrokenCondition_FailsWithTheFileAndCondition(string condition, string expected)
    {
        var path = Write("conditions.json", $$"""{ "conditions": [ {{condition}} ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => ConditionCatalog.Load(path));
        Assert.Contains("conditions.json", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var path = Write("conditions.json", $$"""{ "conditions": [ {{Marked}}, {{Marked}} ] }""");
        Assert.Contains("'marked'", Assert.Throws<InvalidDataException>(() => ConditionCatalog.FromFile(path)).Message);
    }

    [Fact]
    public void Guarded_CannotBeListed()
    {
        var path = Write("conditions.json", """{ "conditions": [ { "id": "guarded", "name": "G", "harmful": false, "modifiers": { "dodge": 9 } } ] }""");
        Assert.Contains("built in", Assert.Throws<InvalidDataException>(() => ConditionCatalog.FromFile(path)).Message);
    }

    // ----- apply_condition in abilities -----

    [Fact]
    public void ApplyCondition_CopiesTheDefinition_AndDefaultsRequiresHitToWhetherTheAbilityAttacks()
    {
        var abilities = AbilityCatalog.Load(Write("abilities.json", """
            { "abilities": [
              { "id": "hex", "name": "Hex", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 6 },
                "effects": [ { "kind": "apply_condition", "condition": "marked", "duration": 2 } ] },
              { "id": "smite", "name": "Smite", "ap": 2, "target": { "kind": "combatant", "filter": "enemy", "range": 1 },
                "effects": [ { "kind": "attack", "roll": "melee" }, { "kind": "apply_condition", "condition": "marked", "duration": 0 } ] },
              { "id": "graze", "name": "Graze", "ap": 2, "target": { "kind": "combatant", "filter": "enemy", "range": 1 },
                "effects": [ { "kind": "attack", "roll": "melee" }, { "kind": "apply_condition", "condition": "marked", "duration": 0, "requiresHit": false } ] }
            ] }
            """), Conditions());

        Assert.Equal((false, 2, "Takes 2 more."), (abilities[0].Effects[0].RequiresHit, abilities[0].Effects[0].Duration, abilities[0].Effects[0].Condition!.Description));
        Assert.True(abilities[1].Effects[1].RequiresHit);
        Assert.False(abilities[2].Effects[1].RequiresHit);
        var rules = (ApplyConditionEffect)AbilityMapping.ToRules(abilities[1]).Effects[1];
        Assert.Equal(("marked", 0, true), (rules.Condition.Id, rules.Duration, rules.RequiresHit));
    }

    [Theory]
    [InlineData("""{ "kind": "combatant", "filter": "enemy", "range": 1 }""", """{ "kind": "apply_condition", "condition": "cursed", "duration": 1 }""", "unknown condition 'cursed'")]
    [InlineData("""{ "kind": "combatant", "filter": "enemy", "range": 1 }""", """{ "kind": "apply_condition", "duration": 1 }""", "'condition' is required")]
    [InlineData("""{ "kind": "combatant", "filter": "enemy", "range": 1 }""", """{ "kind": "apply_condition", "condition": "marked", "duration": -1 }""", "'duration'")]
    [InlineData("""{ "kind": "combatant", "filter": "enemy", "range": 1 }""", """{ "kind": "apply_condition", "condition": "marked" }""", "'duration'")]
    [InlineData("""{ "kind": "combatant", "filter": "enemy", "range": 1 }""", """{ "kind": "apply_condition", "condition": "marked", "duration": 1, "requiresHit": true }""", "needs an \"attack\" effect")]
    [InlineData("""{ "kind": "self" }""", """{ "kind": "apply_condition", "condition": "marked", "duration": 1 }""", "can't apply a harmful condition")]
    [InlineData("""{ "kind": "combatant", "filter": "ally", "range": 3 }""", """{ "kind": "apply_condition", "condition": "marked", "duration": 1 }""", "can't apply a harmful condition")]
    [InlineData("""{ "kind": "combatant", "filter": "enemy", "range": 3 }""", """{ "kind": "apply_condition", "condition": "blessed", "duration": 1 }""", "can't apply a beneficial condition")]
    public void BrokenApplyCondition_FailsWithTheAbility(string target, string effect, string expected)
    {
        var path = Write("abilities.json", $$"""{ "abilities": [ { "id": "x", "name": "X", "ap": 1, "target": {{target}}, "effects": [ {{effect}} ] } ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => AbilityCatalog.Load(path, Conditions()));
        Assert.Contains("abilities.json: x", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void ResonanceThresholdEffects_MustNameAConditionOrAReservedEffect()
    {
        const string Bolt = """{ "id": "x", "name": "X", "ap": 1, "target": { "kind": "combatant", "filter": "enemy", "range": 1 }, "effects": [ { "kind": "attack", "roll": "melee" } ], "resonanceScaling": { "flame": { "thresholdEffects": { "2": "ember", "3": "%EFFECT%" } } } }""";
        Assert.Single(AbilityCatalog.Load(Write("abilities.json", $$"""{ "abilities": [ {{Bolt.Replace("%EFFECT%", "marked")}} ] }"""), Conditions()));

        var path = Write("abilities.json", $$"""{ "abilities": [ {{Bolt.Replace("%EFFECT%", "burnin")}} ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => AbilityCatalog.Load(path, Conditions()));
        Assert.Contains("abilities.json: x", ex.Message);
        Assert.Contains("'burnin' isn't a condition", ex.Message);
    }
}
