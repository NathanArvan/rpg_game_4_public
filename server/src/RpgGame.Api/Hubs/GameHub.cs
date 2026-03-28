using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
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

        timerSvc.ScheduleTimer(
            r.Payload.EncounterId,
            r.SessionId.ToString(),
            TimeSpan.FromMilliseconds(r.Payload.PlanningTimerExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public async Task PlanAction(string joinCode, string actionType, string? targetCombatantId, int? targetX, int? targetY)
    {
        try
        {
            var action = new PlannedAction(actionType, targetCombatantId, targetX, targetY);
            var r = await planningSvc.PlanActionAsync(joinCode, Context.UserIdentifier!, action);

            // Send ActionPlanned to caller only (hidden information)
            await Clients.Caller.SendAsync(HubEvents.Events.ActionPlanned,
                new ActionPlannedPayload(r.CombatantId, r.ActionType));

            // If it's a move, send ghost preview TokenMoved to caller only
            if (actionType == "move" && targetX is not null && targetY is not null)
            {
                await Clients.Caller.SendAsync(HubEvents.Events.TokenMoved,
                    new TokenMovedPayload(r.CombatantId, targetX.Value, targetY.Value));
            }
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
            var r = await planningSvc.LockInAsync(joinCode, Context.UserIdentifier!);
            var groupName = $"session:{r.SessionId}";

            await Clients.Group(groupName).SendAsync(HubEvents.Events.PlayerLockedIn,
                new PlayerLockedInPayload(r.CombatantId, r.CharacterName));

            if (r.AllLockedIn)
            {
                timerSvc.CancelTimer(r.EncounterId);
                await Clients.Group(groupName).SendAsync(HubEvents.Events.AllLockedIn,
                    new AllLockedInPayload(r.EncounterId));
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
            var r = await planningSvc.ForceResolveAsync(joinCode, Context.UserIdentifier!);
            var groupName = $"session:{r.SessionId}";

            timerSvc.CancelTimer(r.EncounterId);
            await Clients.Group(groupName).SendAsync(HubEvents.Events.AllLockedIn,
                new AllLockedInPayload(r.EncounterId));
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
