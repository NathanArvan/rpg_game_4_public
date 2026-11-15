using Microsoft.AspNetCore.SignalR;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Hubs;

namespace RpgGame.Api.Services;

public class RoundOrchestrator(
    IResolutionService resolutionSvc,
    IEncounterService encounterSvc,
    IRedisGameState redisState,
    IPlanningTimer timer,
    IHubContext<GameHub> hubContext,
    ILogger<RoundOrchestrator> logger) : IRoundOrchestrator
{
    /// <summary>How long clients get to play back a resolution before the next phase starts.</summary>
    internal TimeSpan PlaybackDelay { get; init; } = TimeSpan.FromSeconds(3);

    public async Task ResolveAndAdvanceAsync(Guid encounterId, Guid sessionId)
    {
        var group = hubContext.Clients.Group($"session:{sessionId}");

        timer.Cancel(encounterId);
        await group.SendAsync(HubEvents.Events.AllLockedIn, new AllLockedInPayload());

        logger.LogInformation("RoundOrchestrator: resolving encounter={EncounterId}", encounterId);
        var resolution = await resolutionSvc.ResolveRoundAsync(encounterId);
        logger.LogInformation("RoundOrchestrator: resolved — {TickCount} ticks, {DefeatedCount} defeated, next round={NewRound}",
            resolution.Ticks.Count, resolution.DefeatedCombatantIds.Count, resolution.NewRoundNumber);

        await group.SendAsync(HubEvents.Events.ResolutionCompleted, resolution);
        foreach (var defeatedId in resolution.DefeatedCombatantIds)
            await group.SendAsync(HubEvents.Events.CombatantDefeated, new CombatantDefeatedPayload(defeatedId));

        if (PlaybackDelay > TimeSpan.Zero)
            await Task.Delay(PlaybackDelay); // let clients play back before the next phase

        var winningSide = EncounterEndCheck.Evaluate(resolution.UpdatedCombatants);
        if (winningSide is not null)
        {
            var finalRound = resolution.NewRoundNumber - 1;
            await encounterSvc.EndEncounterAsync(encounterId, winningSide, finalRound);
            await group.SendAsync(HubEvents.Events.EncounterEnded,
                new EncounterEndedPayload(encounterId, winningSide, finalRound));
            logger.LogInformation("RoundOrchestrator: encounter={EncounterId} ended after round {Round}, winner={WinningSide}",
                encounterId, finalRound, winningSide);
            return;
        }

        var nextTimerAt = DateTimeOffset.UtcNow.AddSeconds(GameConstants.PlanningTimerSeconds);
        await redisState.StartNextPlanningPhaseAsync(sessionId.ToString(), resolution.NewRoundNumber, nextTimerAt);

        await group.SendAsync(HubEvents.Events.PlanningPhaseStarted, new PlanningPhaseStartedPayload(
            resolution.NewRoundNumber,
            GameConstants.PlanningTimerSeconds,
            resolution.UpdatedCombatants));
        logger.LogInformation("RoundOrchestrator: PlanningPhaseStarted sent for round {Round}", resolution.NewRoundNumber);

        timer.Start(encounterId, sessionId, GameConstants.PlanningTimerSeconds);
    }
}
