using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Hubs;
using RpgGame.Api.Services;

namespace RpgGame.Api.Tests;

public class RoundOrchestratorTests
{
    private readonly Guid _encounterId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Mock<IResolutionService> _resolution = new();
    private readonly Mock<IEncounterService> _encounters = new();
    private readonly Mock<IRedisGameState> _redis = new();
    private readonly Mock<IPlanningTimer> _timer = new();
    private readonly Mock<IClientProxy> _group = new();
    private readonly List<string> _sent = [];
    private readonly List<object?> _sentPayloads = [];

    private RoundOrchestrator CreateOrchestrator()
    {
        _group.Setup(g => g.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) =>
            {
                _sent.Add(method);
                _sentPayloads.Add(args.FirstOrDefault());
            })
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group($"session:{_sessionId}")).Returns(_group.Object);
        var hub = new Mock<IHubContext<GameHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        return new RoundOrchestrator(_resolution.Object, _encounters.Object, _redis.Object, _timer.Object,
            hub.Object, NullLogger<RoundOrchestrator>.Instance)
        {
            PlaybackDelay = TimeSpan.Zero,
        };
    }

    private void ResolutionReturns(int newRound, List<string> defeated, params PlanningCombatantDto[] combatants) =>
        _resolution.Setup(r => r.ResolveRoundAsync(_encounterId))
            .ReturnsAsync(new ResolutionPayload([], newRound, defeated, combatants));

    [Fact]
    public async Task RoundWithBothSidesStanding_StartsNextPlanningPhase()
    {
        ResolutionReturns(3, [],
            new PlanningCombatantDto("p1", "u1", "Hero", 10, 10, false, "player"),
            new PlanningCombatantDto("m1", "gm", "Goblin", 5, 20, false, "monster"));

        await CreateOrchestrator().ResolveAndAdvanceAsync(_encounterId, _sessionId);

        Assert.Equal(
            [HubEvents.Events.AllLockedIn, HubEvents.Events.ResolutionCompleted, HubEvents.Events.PlanningPhaseStarted],
            _sent);
        _timer.Verify(t => t.Cancel(_encounterId), Times.Once);
        _redis.Verify(r => r.StartNextPlanningPhaseAsync(_sessionId.ToString(), 3, It.IsAny<DateTimeOffset>()), Times.Once);
        _timer.Verify(t => t.Start(_encounterId, _sessionId, It.IsAny<int>()), Times.Once);
        _encounters.Verify(e => e.EndEncounterAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RoundThatDefeatsLastEnemy_EndsEncounterInsteadOfStartingNextRound()
    {
        ResolutionReturns(3, ["m1"],
            new PlanningCombatantDto("p1", "u1", "Hero", 10, 10, false, "player"),
            new PlanningCombatantDto("m1", "gm", "Goblin", 0, 20, true, "monster"));

        await CreateOrchestrator().ResolveAndAdvanceAsync(_encounterId, _sessionId);

        Assert.Equal(
            [HubEvents.Events.AllLockedIn, HubEvents.Events.ResolutionCompleted,
             HubEvents.Events.CombatantDefeated, HubEvents.Events.EncounterEnded],
            _sent);

        var defeated = Assert.IsType<CombatantDefeatedPayload>(_sentPayloads[2]);
        Assert.Equal("m1", defeated.CombatantId);

        var ended = Assert.IsType<EncounterEndedPayload>(_sentPayloads[3]);
        Assert.Equal(new EncounterEndedPayload(_encounterId, "party", 2), ended);

        _encounters.Verify(e => e.EndEncounterAsync(_encounterId, "party", 2), Times.Once);
        _redis.Verify(r => r.StartNextPlanningPhaseAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>()), Times.Never);
        _timer.Verify(t => t.Start(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task MutualWipe_EndsAsDraw()
    {
        ResolutionReturns(5, ["p1", "m1"],
            new PlanningCombatantDto("p1", "u1", "Hero", 0, 10, true, "player"),
            new PlanningCombatantDto("m1", "gm", "Goblin", 0, 20, true, "monster"));

        await CreateOrchestrator().ResolveAndAdvanceAsync(_encounterId, _sessionId);

        Assert.Equal(2, _sent.Count(m => m == HubEvents.Events.CombatantDefeated));
        _encounters.Verify(e => e.EndEncounterAsync(_encounterId, "draw", 4), Times.Once);
    }
}
