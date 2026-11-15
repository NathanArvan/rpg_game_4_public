using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Services;

namespace RpgGame.Api.Tests;

public class EncounterEndCheckTests
{
    private static PlanningCombatantDto Player(string id, bool defeated = false) =>
        new(id, "user", id, defeated ? 0 : 10, 10, defeated, "player");

    private static PlanningCombatantDto Monster(string id, bool defeated = false) =>
        new(id, "gm", id, defeated ? 0 : 20, 20, defeated, "monster");

    [Fact]
    public void BothSidesStanding_Continues() =>
        Assert.Null(EncounterEndCheck.Evaluate([Player("p1"), Monster("m1")]));

    [Fact]
    public void AllMonstersDefeated_PartyWins() =>
        Assert.Equal("party", EncounterEndCheck.Evaluate(
            [Player("p1"), Player("p2", defeated: true), Monster("m1", defeated: true), Monster("m2", defeated: true)]));

    [Fact]
    public void AllPlayersDefeated_EnemyWins() =>
        Assert.Equal("enemy", EncounterEndCheck.Evaluate(
            [Player("p1", defeated: true), Player("p2", defeated: true), Monster("m1"), Monster("m2", defeated: true)]));

    [Fact]
    public void BothSidesWipedOut_Draw() =>
        Assert.Equal("draw", EncounterEndCheck.Evaluate([Player("p1", defeated: true), Monster("m1", defeated: true)]));

    [Fact]
    public void NoMonsters_Continues() =>
        Assert.Null(EncounterEndCheck.Evaluate([Player("p1"), Player("p2")]));

    [Fact]
    public void NoPlayers_Continues() =>
        Assert.Null(EncounterEndCheck.Evaluate([Monster("m1")]));

    [Fact]
    public void OneMonsterLeftStanding_Continues() =>
        Assert.Null(EncounterEndCheck.Evaluate([Player("p1"), Monster("m1", defeated: true), Monster("m2")]));
}
