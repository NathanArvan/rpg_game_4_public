using RpgGame.Api.QuickBattle;

namespace RpgGame.Api.Tests;

public class BattlePresetCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("presets-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string file, string json) => File.WriteAllText(Path.Combine(_dir, file), json);

    private const string Valid = """
        { "id": "duel", "name": "Duel", "combatants": [
            { "name": "Hero",   "side": "party", "hp": 10, "damage": "d6", "at": [0, 0] },
            { "name": "Goblin", "side": "enemy", "hp": 5,  "damage": "d4", "at": [0, 1] } ] }
        """;

    [Fact]
    public void ShippedPresets_AllLoad_AndIncludeTheE2EPreset()
    {
        var catalog = BattlePresetCatalog.FromAppDirectory();
        Assert.Contains(catalog.All, p => p.Id == "skirmish");
        Assert.Contains(catalog.All, p => p.Id == "training-dummy");
        Assert.Equal("skirmish", catalog.All[0].Id); // default in the menu
    }

    [Fact]
    public void ValidPreset_Loads_EnemiesDefaultToAi()
    {
        Write("duel.json", Valid);
        var preset = Assert.Single(new BattlePresetCatalog(_dir).All);
        Assert.False(preset.Combatants.Single(c => c.Name == "Hero").IsAiControlled);
        Assert.True(preset.Combatants.Single(c => c.Name == "Goblin").IsAiControlled);
        Assert.Equal(10, preset.Combatants.Single(c => c.Name == "Goblin").Speed); // default speed
    }

    [Theory]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "hp": 5, "damage": "d6", "at": [0, 0] } ] }""", "at least one enemy")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "hp": 5, "damage": "d6", "at": [0, 0] }, { "name": "B", "side": "enemy", "hp": 5, "damage": "d6", "at": [0, 0] } ] }""", "already taken")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "hp": 5, "damage": "d6", "at": [0, 20] }, { "name": "B", "side": "enemy", "hp": 5, "damage": "d6", "at": [1, 0] } ] }""", "outside the grid")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "party", "hp": 5, "damage": "2d6", "at": [0, 0] }, { "name": "B", "side": "enemy", "hp": 5, "damage": "d6", "at": [1, 0] } ] }""", "'damage'")]
    [InlineData("""{ "id": "x", "name": "X", "combatants": [ { "name": "A", "side": "ally", "hp": 5, "damage": "d6", "at": [0, 0] } ] }""", "'side'")]
    [InlineData("""{ "name": "X", "combatants": [] }""", "'id'")]
    [InlineData("""{ not json""", "invalid JSON")]
    public void BrokenPreset_FailsWithTheFileName(string json, string expected)
    {
        Write("broken.json", json);
        var ex = Assert.Throws<InvalidDataException>(() => new BattlePresetCatalog(_dir));
        Assert.Contains("broken.json", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        Write("a.json", Valid);
        Write("b.json", Valid);
        var ex = Assert.Throws<InvalidDataException>(() => new BattlePresetCatalog(_dir));
        Assert.Contains("'duel'", ex.Message);
    }
}
