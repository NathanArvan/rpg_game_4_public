using Microsoft.AspNetCore.SignalR;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Hubs;

namespace RpgGame.Api.Services;

public class RoundOrchestrator(
    IResolutionService resolutionSvc,
    IEncounterService encounterSvc,
    IAiPlanningService aiPlanningSvc,
    IRedisGameState redisState,
    IPlanningTimer timer,
    IHubContext<GameHub> hubContext,
    ILogger<RoundOrchestrator> logger) : IRoundOrchestrator
{
    /// <summary>
    /// How long clients get to play back each tick of a resolution before the next phase starts.
    /// A round with an AP queue of n actions has n ticks (3B.2), so the wait is n times this.
    /// </summary>
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

        if (PlaybackDelay > TimeSpan.Zero) // let clients play back every tick before the next phase
            await Task.Delay(PlaybackDelay * Math.Max(1, resolution.Ticks.Count));

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

        // Quick Battle turns the planning timer off; 0 seconds tells clients there's no countdown.
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId.ToString());
        var timerSeconds = activeEncounter?.PlanningTimerEnabled == false ? 0 : GameConstants.PlanningTimerSeconds;

        var nextTimerAt = DateTimeOffset.UtcNow.AddSeconds(timerSeconds);
        await redisState.StartNextPlanningPhaseAsync(sessionId.ToString(), resolution.NewRoundNumber, nextTimerAt);

        await StartPlanningPhaseAsync(encounterId, sessionId, new PlanningPhaseStartedPayload(
            resolution.NewRoundNumber,
            timerSeconds,
            resolution.UpdatedCombatants,
            RulesWireMapping.ApCosts()));
    }

    public async Task StartPlanningPhaseAsync(Guid encounterId, Guid sessionId, PlanningPhaseStartedPayload payload)
    {
        var group = hubContext.Clients.Group($"session:{sessionId}");

        await group.SendAsync(HubEvents.Events.PlanningPhaseStarted, payload);
        logger.LogInformation("RoundOrchestrator: PlanningPhaseStarted sent for round {Round}", payload.RoundNumber);

        if (payload.PlanningTimerDurationSeconds > 0)
            timer.Start(encounterId, sessionId, payload.PlanningTimerDurationSeconds);

        // After PlanningPhaseStarted, which resets each client's lock-in list.
        var aiLockIns = await aiPlanningSvc.PlanAndLockInAsync(encounterId);
        foreach (var lockIn in aiLockIns)
            await group.SendAsync(HubEvents.Events.PlayerLockedIn, new PlayerLockedInPayload(lockIn.CharacterName));

        // Combatants whose conditions stop them acting this round (Stunned) skip it: locked in with an empty plan
        // (3B.4 PR 3, SPRINT_3B4 C8). If that leaves nobody to wait for, the round resolves on its own.
        var skipped = await aiPlanningSvc.LockInUnableToActAsync(encounterId);
        foreach (var lockIn in skipped?.LockedIn ?? [])
            await group.SendAsync(HubEvents.Events.PlayerLockedIn, new PlayerLockedInPayload(lockIn.CharacterName));
        if (skipped is { AllLockedIn: true })
        {
            logger.LogInformation("RoundOrchestrator: nobody can act in round {Round}; resolving", payload.RoundNumber);
            await ResolveAndAdvanceAsync(encounterId, sessionId);
        }
    }
}
