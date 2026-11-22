using System.Text.Json;
using RpgGame.Api.Contracts.Hub;
using static RpgGame.Api.Contracts.Hub.HubEvents;

namespace RpgGame.Api.IntegrationTests.Hub;

/// <summary>
/// The wire JSON of a resolution against the client's TS contracts, and checks that the checker
/// itself catches drift (so a passing shape check means something).
/// </summary>
[Collection(ApiCollection.Name)]
public class ContractShapeTests(ApiFactory api)
{
    [Fact]
    public async Task ResolutionCompleted_on_the_wire_has_exactly_the_client_contract_fields()
    {
        await using var client = await HubClient.ConnectNewUserAsync(api);
        var battle = await client.InvokeAsync<QuickBattleStartedResult>(Methods.StartQuickBattle, "it-duel");
        var ids = (await client.NextAsync<EncounterStartedPayload>(Events.EncounterStarted))
            .Combatants.ToDictionary(c => c.CharacterName, c => c.Id);

        // A move and an attack, so movements, attacks and rolls are all on the wire.
        api.Dice.Script(15, 4);
        await client.InvokeAsync(Methods.PlanAction, battle.JoinCode,
            new PlannedActionArray([new("move", new PositionDto(8, 9), null), new("melee_attack", null, ids["Goblin"])]), ids["Hero"]);
        await client.InvokeAsync(Methods.LockIn, battle.JoinCode);
        await client.InvokeAsync(Methods.LockInEnemies, battle.JoinCode);
        await client.NextAsync<PlanningPhaseStartedPayload>(Events.PlanningPhaseStarted, p => p.RoundNumber == 2);

        var resolution = client.Events.Single(e => e.Name == Events.ResolutionCompleted).Payload;
        Assert.Empty(ContractShape.Check(resolution, "ResolutionPayload", ContractShape.Shipped));
        Assert.True(resolution.GetProperty("ticks")[0].GetProperty("movements").GetArrayLength() > 0);
        Assert.True(resolution.GetProperty("ticks")[1].GetProperty("attacks").GetArrayLength() > 0);
        ContractShape.AssertEventsMatch(client.Events);
    }

    [Fact]
    public void Every_broadcast_has_a_client_interface()
    {
        foreach (var name in HubClient.AllEventNames)
        {
            Assert.True(ContractShape.PayloadInterface.TryGetValue(name, out var iface), $"{name} has no entry in ContractShape.PayloadInterface");
            Assert.True(ContractShape.Shipped.ContainsKey(iface!), $"{name}: the client contracts have no interface {iface}");
        }
    }

    private const string Ts = """
        export interface Outer {
          id: string;
          inner: Inner;
          items: Inner[];
          byId: { [key: string]: Inner };
          note: string | null;
        }

        export interface Inner {
          hp: number;
        }

        export interface Empty {
        }
        """;

    private static IReadOnlyList<string> CheckOuter(string json) =>
        ContractShape.Check(JsonDocument.Parse(json).RootElement, "Outer", ContractShape.Parse(Ts));

    [Fact]
    public void Checker_accepts_a_matching_payload()
    {
        Assert.Empty(CheckOuter("""{"id":"a","inner":{"hp":1},"items":[{"hp":2}],"byId":{"x":{"hp":3}},"note":null}"""));
        Assert.Empty(ContractShape.Check(JsonDocument.Parse("{}").RootElement, "Empty", ContractShape.Parse(Ts)));
    }

    [Theory]
    [InlineData("""{"id":"a","inner":{"hp":1},"items":[],"byId":{},"note":null,"extra":1}""", "Outer.extra: sent by the server but not declared")]
    [InlineData("""{"inner":{"hp":1},"items":[],"byId":{},"note":null}""", "Outer.id: declared by the client's Outer but not sent")]
    [InlineData("""{"Id":"a","inner":{"hp":1},"items":[],"byId":{},"note":null}""", "Outer.Id: sent by the server but not declared")]
    [InlineData("""{"id":"a","inner":{"HP":1},"items":[],"byId":{},"note":null}""", "Outer.inner.HP: sent by the server")]
    [InlineData("""{"id":"a","inner":{"hp":1},"items":[{"hp":"x"}],"byId":{},"note":null}""", "Outer.items[0].hp: expected number")]
    [InlineData("""{"id":"a","inner":{"hp":1},"items":[],"byId":{"k":{}},"note":null}""", "Outer.byId[k].hp: declared")]
    [InlineData("""{"id":null,"inner":{"hp":1},"items":[],"byId":{},"note":null}""", "Outer.id: null, but the client's type is string")]
    public void Checker_reports_drift(string json, string expected)
    {
        var problems = CheckOuter(json);
        Assert.Contains(problems, p => p.StartsWith(expected));
    }
}
