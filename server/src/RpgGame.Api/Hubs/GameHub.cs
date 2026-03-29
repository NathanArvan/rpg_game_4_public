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

            await Clients.Group(groupName).SendAsync(HubEvents.Events.PlayerLockedIn, new PlayerLockedInPayload(result.CharacterName));

            if (result.AllLockedIn)
            {
                timerSvc.Cancel(Guid.Parse(result.EncounterId));
                await Clients.Group(groupName).SendAsync(HubEvents.Events.AllLockedIn, new AllLockedInPayload());
                // Resolution will be wired here in 3A.2
            }
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
    }

    public async Task ForceResolve(string joinCode)
    {
        try
        {
            var result = await planningSvc.ForceResolveAsync(joinCode, Context.UserIdentifier!);
            timerSvc.Cancel(Guid.Parse(result.EncounterId));

            var groupName = $"session:{result.SessionId}";
            await Clients.Group(groupName).SendAsync(HubEvents.Events.AllLockedIn, new AllLockedInPayload());
            // Resolution will be wired here in 3A.2
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("Client disconnected: {ConnectionId}, error: {Error}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
