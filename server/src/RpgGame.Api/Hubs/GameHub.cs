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
    IRoundOrchestrator roundOrchestrator,
    IAiPlanningService aiPlanningSvc,
    IQuickBattleService quickBattleSvc,
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
        await roundOrchestrator.StartPlanningPhaseAsync(Guid.Parse(r.Payload.EncounterId), r.SessionId, r.PlanningPayload);
    }

    public IReadOnlyList<QuickBattlePresetDto> GetQuickBattlePresets() => quickBattleSvc.ListPresets();

    /// <summary>
    /// One-click battle: new campaign + session + encounter from a preset, with the caller controlling
    /// the whole party against AI enemies. Returns the new session so the client can act in it.
    /// </summary>
    public async Task<QuickBattleStartedResult> StartQuickBattle(string presetId)
    {
        QuickBattleStart r;
        try
        {
            r = await quickBattleSvc.StartAsync(Context.UserIdentifier!, presetId);
        }
        catch (GameActionException ex)
        {
            throw new HubException(ex.Message);
        }
        var groupName = $"session:{r.Session.GameSessionId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        await Clients.Group(groupName).SendAsync(HubEvents.Events.EncounterStarted, r.Encounter.Payload);
        await roundOrchestrator.StartPlanningPhaseAsync(
            Guid.Parse(r.Encounter.Payload.EncounterId), r.Session.GameSessionId, r.Encounter.PlanningPayload);
        return r.Session;
    }

    /// <param name="combatantId">Which of the caller's characters this is for; null if they control only one.</param>
    public async Task PlanAction(string joinCode, PlannedActionArray plannedAction, string? combatantId)
    {
        try
        {
            await planningSvc.PlanActionAsync(joinCode, Context.UserIdentifier!, plannedAction, combatantId);
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

            var names = result.LockedInNames ?? [result.CharacterName];
            logger.LogInformation("LockIn: {Names} locked in (allLockedIn={AllLockedIn})", string.Join(", ", names), result.AllLockedIn);
            foreach (var name in names)
                await Clients.Group(groupName).SendAsync(HubEvents.Events.PlayerLockedIn, new PlayerLockedInPayload(name));

            if (result.AllLockedIn)
                await roundOrchestrator.ResolveAndAdvanceAsync(Guid.Parse(result.EncounterId), result.SessionId);
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
            logger.LogInformation("ForceResolve: triggered by user={UserId}", Context.UserIdentifier);
            await roundOrchestrator.ResolveAndAdvanceAsync(Guid.Parse(result.EncounterId), result.SessionId);
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

    public async Task AddMonster(string joinCode, string monsterName, bool isAiControlled)
    {
        logger.LogInformation("AddMonster hub: joinCode={JoinCode} userId={UserId} monsterName={MonsterName}", joinCode, Context.UserIdentifier, monsterName);
        try
        {
            var r = await encounterSvc.AddMonsterAsync(joinCode, Context.UserIdentifier!, monsterName, isAiControlled);
            var groupName = $"session:{r.SessionId}";
            logger.LogInformation("AddMonster hub: broadcasting MonsterAdded to group {GroupName}", groupName);
            await Clients.Group(groupName).SendAsync(HubEvents.Events.MonsterAdded,
                new MonsterAddedPayload(r.MonsterDto, r.SpawnPosition));

            // Added mid-round (monsters can only be added during planning): an AI monster plans now.
            if (isAiControlled && !string.IsNullOrEmpty(r.EncounterId))
            {
                var lockIns = await aiPlanningSvc.PlanAndLockInAsync(Guid.Parse(r.EncounterId), r.MonsterDto.Id);
                foreach (var lockIn in lockIns)
                    await Clients.Group(groupName).SendAsync(HubEvents.Events.PlayerLockedIn, new PlayerLockedInPayload(lockIn.CharacterName));
            }
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in AddMonster for joinCode={JoinCode}, user={UserId}", joinCode, Context.UserIdentifier);
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload("An internal error occurred while adding the monster."));
        }
    }

    public async Task PlanEnemyAction(string joinCode, string combatantId, PlannedActionArray plannedAction)
    {
        logger.LogInformation("PlanEnemyAction hub: joinCode={JoinCode} userId={UserId} combatantId={CombatantId}", joinCode, Context.UserIdentifier, combatantId);
        try
        {
            await planningSvc.PlanEnemyActionAsync(joinCode, Context.UserIdentifier!, combatantId, plannedAction);
            // No broadcast — enemy plans are hidden from players.
        }
        catch (GameActionException ex) when (ex.IsActionRejected)
        {
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in PlanEnemyAction for joinCode={JoinCode}, user={UserId}", joinCode, Context.UserIdentifier);
            await Clients.Caller.SendAsync(HubEvents.Events.ActionRejected, new ActionRejectedPayload("An internal error occurred while planning enemy action."));
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
                await roundOrchestrator.ResolveAndAdvanceAsync(Guid.Parse(result.EncounterId), result.SessionId);
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

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("Client disconnected: {ConnectionId}, error: {Error}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
