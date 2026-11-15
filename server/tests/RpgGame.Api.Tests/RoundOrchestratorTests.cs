using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Hubs;
using RpgGame.Api.Models;
using RpgGame.Api.Services;

namespace RpgGame.Api.Tests;

public class RoundOrchestratorTests
{
    private readonly Guid _encounterId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Mock<IResolutionService> _resolution = new();
    private readonly Mock<IEncounterService> _encounters = new();
    private readonly Mock<IAiPlanningService> _ai = new();
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

        _ai.Setup(a => a.PlanAndLockInAsync(It.IsAny<Guid>(), It.IsAny<string?>()))
            .ReturnsAsync(Array.Empty<AiLockIn>());

        return new RoundOrchestrator(_resolution.Object, _encounters.Object, _ai.Object, _redis.Object, _timer.Object,
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
        _ai.Verify(a => a.PlanAndLockInAsync(_encounterId, null), Times.Once);
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
        _ai.Verify(a => a.PlanAndLockInAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task StartPlanningPhase_AiLocksInAfterPlanningPhaseStarted()
    {
        var orchestrator = CreateOrchestrator();
        _ai.Setup(a => a.PlanAndLockInAsync(_encounterId, null))
            .ReturnsAsync([new AiLockIn("m1", "Goblin"), new AiLockIn("m2", "Orc")]);

        await orchestrator.StartPlanningPhaseAsync(_encounterId, _sessionId, new PlanningPhaseStartedPayload(1, 300, []));

        Assert.Equal(
            [HubEvents.Events.PlanningPhaseStarted, HubEvents.Events.PlayerLockedIn, HubEvents.Events.PlayerLockedIn],
            _sent);
        Assert.Equal("Goblin", Assert.IsType<PlayerLockedInPayload>(_sentPayloads[1]).CharacterName);
        Assert.Equal("Orc", Assert.IsType<PlayerLockedInPayload>(_sentPayloads[2]).CharacterName);
        _timer.Verify(t => t.Start(_encounterId, _sessionId, 300), Times.Once);
    }

    [Fact]
    public async Task TimerOff_NextRoundHasNoCountdownAndNoTimer()
    {
        ResolutionReturns(3, [],
            new PlanningCombatantDto("p1", "u1", "Hero", 10, 10, false, "player"),
            new PlanningCombatantDto("m1", "u1", "Goblin", 5, 20, false, "monster"));
        _redis.Setup(r => r.GetActiveEncounterAsync(_sessionId.ToString())).ReturnsAsync(
            new ActiveEncounterEntry(_encounterId.ToString(), "between_rounds", 3, DateTimeOffset.UtcNow, [], PlanningTimerEnabled: false));

        await CreateOrchestrator().ResolveAndAdvanceAsync(_encounterId, _sessionId);

        var payload = Assert.IsType<PlanningPhaseStartedPayload>(_sentPayloads[_sent.IndexOf(HubEvents.Events.PlanningPhaseStarted)]);
        Assert.Equal(0, payload.PlanningTimerDurationSeconds);
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
