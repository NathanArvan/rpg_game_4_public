using RpgGame.Api.CombatProfiles;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

public class CombatProfileCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("profiles-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "profiles.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string Defaults = """
        { "id": "skirmisher", "name": "S", "hp": 1, "move": 1, "speed": 1, "dodge": 1, "damage": "d4" },
        { "id": "grunt", "name": "G", "hp": 1, "move": 1, "speed": 1, "dodge": 1, "damage": "d4" }
        """;

    [Fact]
    public void ShippedProfiles_Load_AndDifferMeaningfully()
    {
        var catalog = TestProfiles.Catalog;
        Assert.Equal(CombatProfileCatalog.DefaultPartyId, catalog.DefaultParty.Id);
        Assert.Equal(CombatProfileCatalog.DefaultEnemyId, catalog.DefaultEnemy.Id);
        foreach (var id in new[] { "bruiser", "skirmisher", "scout", "grunt", "brute" })
            Assert.NotNull(catalog.Find(id));

        // Speed tiebreaks and movement only matter if profiles differ (roadmap 3B.1).
        var party = catalog.All.Where(p => p.Side == "party").ToList();
        Assert.Equal(party.Count, party.Select(p => p.Speed).Distinct().Count());
        Assert.True(party.Select(p => p.MovementRange).Distinct().Count() > 1);
    }

    [Fact]
    public void Snapshot_CopiesEveryValue_AndMapsToTheRulesProfile()
    {
        var bruiser = TestProfiles.Catalog.Find("bruiser")!;
        var snapshot = bruiser.Snapshot();
        Assert.Equal(("bruiser", "Bruiser"), (snapshot.ProfileId, snapshot.ProfileName));

        var rules = CombatProfileMapping.ToRules(snapshot);
        Assert.Equal(
            new CombatProfile(bruiser.MaxHp, bruiser.MovementRange, bruiser.Speed, bruiser.MeleeAccuracy,
                bruiser.RangedAccuracy, bruiser.Dodge, DamageDie.Parse(bruiser.BaseDamage), bruiser.MaxAp),
            rules with { Abilities = [] });
        // The loadout (3B.3) is copied in full and reaches the rules engine.
        Assert.Equal(["power_strike", "cleave"], rules.Abilities.Select(a => a.Id));
        Assert.NotSame(snapshot, bruiser.Snapshot());
        Assert.NotSame(snapshot.Abilities![0], bruiser.Snapshot().Abilities![0]);
    }

    [Fact]
    public void ShippedProfiles_HaveApBudgets_ThatDiffer()
    {
        var all = TestProfiles.Catalog.All;
        Assert.All(all, p => Assert.InRange(p.MaxAp, CombatProfileCatalog.MinAp, CombatProfileCatalog.MaxApLimit));
        Assert.True(all.Select(p => p.MaxAp).Distinct().Count() > 1);
        Assert.Equal(all.Select(p => p.MaxAp), all.Select(p => p.Snapshot().MaxAp!.Value));
    }

    [Fact]
    public void StoredProfileWithoutMaxAp_GetsTheDefaultBudget()
    {
        var old = TestProfiles.Catalog.Find("scout")!.Snapshot();
        old.MaxAp = null; // stored before 3B.2
        Assert.Equal(CombatProfile.DefaultMaxAp, CombatProfileMapping.ToRules(old).MaxAp);
    }

    [Fact]
    public void MissingStoredProfile_MapsToTheDefault() =>
        Assert.Equal(CombatProfile.Default, CombatProfileMapping.ToRules(null));

    [Fact]
    public void OptionalFields_Default()
    {
        var p = Assert.Single(CombatProfileCatalog.Load(Write("""
            { "profiles": [ { "id": "x", "name": "X", "hp": 5, "move": 3, "speed": 9, "dodge": 11, "damage": "d6" } ] }
            """)));
        Assert.Equal(("any", "", 0, 0, CombatProfile.DefaultMaxAp), (p.Side, p.Description, p.MeleeAccuracy, p.RangedAccuracy, p.MaxAp));
    }

    [Theory]
    [InlineData("""{ "id": "x", "name": "X", "hp": 0, "move": 3, "speed": 9, "dodge": 11, "damage": "d6" }""", "'hp'")]
    [InlineData("""{ "id": "x", "name": "X", "hp": 5, "move": 0, "speed": 9, "dodge": 11, "damage": "d6" }""", "'move'")]
    [InlineData("""{ "id": "x", "name": "X", "hp": 5, "move": 3, "dodge": 11, "damage": "d6" }""", "'speed'")]
    [InlineData("""{ "id": "x", "name": "X", "hp": 5, "move": 3, "speed": 9, "damage": "d6" }""", "'dodge'")]
    [InlineData("""{ "id": "x", "name": "X", "hp": 5, "move": 3, "speed": 9, "dodge": 11, "damage": "2d6" }""", "'damage'")]
    [InlineData("""{ "id": "x", "name": "X", "side": "ally", "hp": 5, "move": 3, "speed": 9, "dodge": 11, "damage": "d6" }""", "'side'")]
    [InlineData("""{ "name": "X", "hp": 5, "move": 3, "speed": 9, "dodge": 11, "damage": "d6" }""", "'id'")]
    [InlineData("""{ "id": "x", "name": "X", "hp": 5, "move": 3, "speed": 9, "dodge": 11, "damage": "d6", "ap": 0 }""", "'ap'")]
    [InlineData("""{ "id": "x", "name": "X", "hp": 5, "move": 3, "speed": 9, "dodge": 11, "damage": "d6", "ap": 11 }""", "'ap'")]
    public void BrokenProfile_FailsWithTheFileName(string profile, string expected)
    {
        var path = Write($$"""{ "profiles": [ {{profile}} ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => CombatProfileCatalog.Load(path));
        Assert.Contains("profiles.json", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var path = Write($$"""{ "profiles": [ {{Defaults}}, { "id": "grunt", "name": "G2", "hp": 1, "move": 1, "speed": 1, "dodge": 1, "damage": "d4" } ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => CombatProfileCatalog.FromFile(path));
        Assert.Contains("'grunt'", ex.Message);
    }

    [Fact]
    public void MissingDefaultProfile_IsRejected()
    {
        var path = Write("""{ "profiles": [ { "id": "grunt", "name": "G", "hp": 1, "move": 1, "speed": 1, "dodge": 1, "damage": "d4" } ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => CombatProfileCatalog.FromFile(path));
        Assert.Contains("'skirmisher'", ex.Message);
    }
}
