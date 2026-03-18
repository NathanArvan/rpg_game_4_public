using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Services;

namespace RpgGame.Api.Hubs;

[Authorize]
public class GameHub(ISessionService sessionSvc, IEncounterService encounterSvc, ICombatService combatSvc, ILogger<GameHub> logger) : Hub
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
            await Clients.Caller.SendAsync("PlayerPresenceChanged", p);

        logger.LogInformation("Broadcasting PlayerPresenceChanged to group {GroupName}", groupName);
        await Clients.Group(groupName).SendAsync("PlayerPresenceChanged", r.NewPlayerPayload);
    }

    public async Task StartEncounter(string joinCode)
    {
        var r = await encounterSvc.StartEncounterAsync(joinCode, Context.UserIdentifier!);
        await Clients.Group($"session:{r.SessionId}").SendAsync("EncounterStarted", r.Payload);
    }

    public async Task MoveToken(string joinCode, string combatantId, int targetX, int targetY)
    {
        try
        {
            var r = await encounterSvc.MoveTokenAsync(joinCode, Context.UserIdentifier!, combatantId, targetX, targetY);
            await Clients.Group($"session:{r.SessionId}").SendAsync("TokenMoved", r.Payload);
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload(ex.Message));
        }
    }

    public async Task DeclareAttack(string joinCode, string targetCombatantId)
    {
        try
        {
            var r = await combatSvc.DeclareAttackAsync(joinCode, Context.UserIdentifier!, targetCombatantId);
            await Clients.Group($"session:{r.SessionId}").SendAsync("AttackResolved", r.AttackPayload);
            if (r.DefeatedPayload is not null)
                await Clients.Group($"session:{r.SessionId}").SendAsync("CombatantDefeated", r.DefeatedPayload);
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload(ex.Message));
        }
    }

    public async Task EndTurn(string joinCode)
    {
        var result = await encounterSvc.EndTurnAsync(joinCode, Context.UserIdentifier!);
        switch (result)
        {
            case TurnAdvancedResult r:
                await Clients.Group($"session:{r.SessionId}").SendAsync("TurnAdvanced", r.Payload);
                break;
            case EncounterEndedResult r:
                await Clients.Group($"session:{r.SessionId}").SendAsync("EncounterEnded", r.Payload);
                break;
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("Client disconnected: {ConnectionId}, error: {Error}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
