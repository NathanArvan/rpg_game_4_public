using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Services;

namespace RpgGame.Api.Hubs;

[Authorize]
public class GameHub(
    ISessionService sessionSvc,
    IEncounterService encounterSvc,
    IPlanningService planningSvc,
    IResolutionService resolutionSvc,
    IRedisGameState redisState,
    PlanningTimerService timerSvc,
    ILogger<GameHub> logger) : Hub
{
    public override Task OnConnectedAsync()
    {
        logger.LogInformation("Client connected: {ConnectionId}, User: {UserId}", Context.ConnectionId, Context.UserIdentifier);
        return base.OnConnectedAsync();
    }

    public async Task JoinSession(string joinCode, string displayName, string? characterId)
    {
        logger.LogInformation("JoinSession called: joinCode={JoinCode}, displayName={DisplayName}, userId={UserId}", joinCode, displayName, Context.UserIdentifier);

        var r = await sessionSvc.JoinSessionAsync(joinCode, Context.UserIdentifier!, displayName, characterId);

        var groupName = $"session:{r.SessionId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        foreach (var p in r.ExistingPlayerPayloads)
            await Clients.Caller.SendAsync(HubEvents.Events.PlayerPresenceChanged, p);

        logger.LogInformation("Broadcasting PlayerPresenceChanged to group {GroupName}", groupName);
        await Clients.Group(groupName).SendAsync(HubEvents.Events.PlayerPresenceChanged, r.NewPlayerPayload);
    }

    public async Task StartEncounter(string joinCode)
    {
        var r = await encounterSvc.StartEncounterAsync(joinCode, Context.UserIdentifier!);
        var groupName = $"session:{r.SessionId}";

        await Clients.Group(groupName).SendAsync(HubEvents.Events.EncounterStarted, r.Payload);
        await Clients.Group(groupName).SendAsync(HubEvents.Events.PlanningPhaseStarted, r.PlanningPayload);

        var encounterId = Guid.Parse(r.Payload.EncounterId);
        timerSvc.Start(encounterId, r.SessionId, r.PlanningPayload.PlanningTimerDurationSeconds);
    }

    public async Task PlanAction(string joinCode, PlannedActionArray plannedAction)
    {
        try
        {
            await planningSvc.PlanActionAsync(joinCode, Context.UserIdentifier!, plannedAction);
            // No broadcast — plans are private until lock-in.
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
    }

    public async Task LockIn(string joinCode)
    {
        try
        {
            var result = await planningSvc.LockInAsync(joinCode, Context.UserIdentifier!);
            var groupName = $"session:{result.SessionId}";

            logger.LogInformation("LockIn: {CharacterName} locked in (allLockedIn={AllLockedIn})", result.CharacterName, result.AllLockedIn);
            await Clients.Group(groupName).SendAsync(HubEvents.Events.PlayerLockedIn, new PlayerLockedInPayload(result.CharacterName));

            if (result.AllLockedIn)
            {
                var encounterId = Guid.Parse(result.EncounterId);
                timerSvc.Cancel(encounterId);
                await Clients.Group(groupName).SendAsync(HubEvents.Events.AllLockedIn, new AllLockedInPayload());
                await RunResolutionAsync(groupName, encounterId, result.SessionId);
            }
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in LockIn for joinCode={JoinCode}, user={UserId}", joinCode, Context.UserIdentifier);
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload("An internal error occurred during lock-in."));
        }
    }

    public async Task ForceResolve(string joinCode)
    {
        try
        {
            var result = await planningSvc.ForceResolveAsync(joinCode, Context.UserIdentifier!);
            var encounterId = Guid.Parse(result.EncounterId);
            timerSvc.Cancel(encounterId);

            logger.LogInformation("ForceResolve: triggered by user={UserId}", Context.UserIdentifier);
            var groupName = $"session:{result.SessionId}";
            await Clients.Group(groupName).SendAsync(HubEvents.Events.AllLockedIn, new AllLockedInPayload());
            await RunResolutionAsync(groupName, encounterId, result.SessionId);
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in ForceResolve for joinCode={JoinCode}, user={UserId}", joinCode, Context.UserIdentifier);
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload("An internal error occurred during force resolve."));
        }
    }

    public async Task AddMonster(string joinCode, string monsterName)
    {
        try
        {
            var r = await encounterSvc.AddMonsterAsync(joinCode, Context.UserIdentifier!, monsterName);
            var groupName = $"session:{r.SessionId}";
            await Clients.Group(groupName).SendAsync(HubEvents.Events.MonsterAdded,
                new MonsterAddedPayload(r.MonsterDto, r.SpawnPosition));
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
    }

    public async Task PlanEnemyAction(string joinCode, string combatantId, PlannedActionArray plannedAction)
    {
        try
        {
            await planningSvc.PlanEnemyActionAsync(joinCode, Context.UserIdentifier!, combatantId, plannedAction);
            // No broadcast — enemy plans are hidden from players.
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
    }

    public async Task LockInEnemies(string joinCode)
    {
        try
        {
            var result = await planningSvc.LockInEnemiesAsync(joinCode, Context.UserIdentifier!);
            var groupName = $"session:{result.SessionId}";

            logger.LogInformation("LockInEnemies: GM locked in enemies (allLockedIn={AllLockedIn})", result.AllLockedIn);
            await Clients.Group(groupName).SendAsync(HubEvents.Events.PlayerLockedIn, new PlayerLockedInPayload(result.CharacterName));

            if (result.AllLockedIn)
            {
                var encounterId = Guid.Parse(result.EncounterId);
                timerSvc.Cancel(encounterId);
                await Clients.Group(groupName).SendAsync(HubEvents.Events.AllLockedIn, new AllLockedInPayload());
                await RunResolutionAsync(groupName, encounterId, result.SessionId);
            }
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in LockInEnemies for joinCode={JoinCode}, user={UserId}", joinCode, Context.UserIdentifier);
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload("An internal error occurred during enemy lock-in."));
        }
    }

    private async Task RunResolutionAsync(string groupName, Guid encounterId, Guid sessionId)
    {
        const int planningTimerSeconds = 30;

        logger.LogInformation("RunResolution: starting for encounter={EncounterId}", encounterId);

        var resolution = await resolutionSvc.ResolveRoundAsync(encounterId);
        logger.LogInformation("RunResolution: resolved — {TickCount} ticks, {DefeatedCount} defeated, next round={NewRound}",
            resolution.Ticks.Count, resolution.DefeatedCombatantIds.Count, resolution.NewRoundNumber);

        await Clients.Group(groupName).SendAsync(HubEvents.Events.ResolutionCompleted, resolution);
        logger.LogInformation("RunResolution: ResolutionCompleted broadcast sent");

        await Task.Delay(3000); // allow clients to play back before the next planning phase starts

        var nextTimerAt = DateTimeOffset.UtcNow.AddSeconds(planningTimerSeconds);
        await redisState.StartNextPlanningPhaseAsync(sessionId.ToString(), resolution.NewRoundNumber, nextTimerAt);

        var planningPayload = new PlanningPhaseStartedPayload(
            resolution.NewRoundNumber,
            planningTimerSeconds,
            resolution.UpdatedCombatants);
        await Clients.Group(groupName).SendAsync(HubEvents.Events.PlanningPhaseStarted, planningPayload);
        logger.LogInformation("RunResolution: PlanningPhaseStarted broadcast sent for round {Round}", resolution.NewRoundNumber);

        timerSvc.Start(encounterId, sessionId, planningTimerSeconds);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("Client disconnected: {ConnectionId}, error: {Error}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
