using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Services;

public class PlanningService(AppDbContext db, IRedisGameState redisState) : IPlanningService
{
    public async Task<PlanActionResult> PlanActionAsync(string joinCode, string userId, PlannedAction action)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        var combatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.UserId == userId)
            ?? throw new GameActionException("You are not in this encounter.", isActionRejected: true);

        if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, combatantRef.Id))
            throw new GameActionException("You are defeated.", isActionRejected: true);

        if (await redisState.IsLockedInAsync(activeEncounter.EncounterId, combatantRef.Id))
            throw new GameActionException("You are already locked in.", isActionRejected: true);

        await ValidateActionAsync(action, combatantRef.Id, activeEncounter);

        await redisState.SetPlannedActionAsync(activeEncounter.EncounterId, combatantRef.Id, action);

        return new PlanActionResult(gameSession.Id, combatantRef.Id, action.ActionType);
    }

    public async Task<LockInResult> LockInAsync(string joinCode, string userId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        var combatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.UserId == userId)
            ?? throw new GameActionException("You are not in this encounter.", isActionRejected: true);

        if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, combatantRef.Id))
            throw new GameActionException("You are defeated.", isActionRejected: true);

        var plannedAction = await redisState.GetPlannedActionAsync(activeEncounter.EncounterId, combatantRef.Id);
        if (plannedAction is null)
            throw new GameActionException("You must plan an action before locking in.", isActionRejected: true);

        await redisState.AddLockedInAsync(activeEncounter.EncounterId, combatantRef.Id);

        var allLockedIn = await AreAllLockedInAsync(activeEncounter, activeEncounter.EncounterId);
        if (allLockedIn)
        {
            var updated = activeEncounter with { RoundPhase = "resolving" };
            await redisState.SetActiveEncounterAsync(sessionId, updated);
        }

        // Find character name for broadcast
        var combatantDb = await db.EncounterCombatants.FindAsync(GuidParser.TryParse(combatantRef.Id));
        var characterName = combatantDb?.CharacterName ?? "Unknown";

        return new LockInResult(gameSession.Id, combatantRef.Id, characterName, allLockedIn, activeEncounter.EncounterId);
    }

    public async Task<PlanEnemyActionResult> PlanEnemyActionAsync(string joinCode, string callerUserId, string combatantId, PlannedAction action)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var member = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == callerUserId);

        if (member is null || member.Role != Domain.Entities.CampaignRole.Gm)
            throw new GameActionException("Only the GM can plan enemy actions.", isActionRejected: true);

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        var combatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == combatantId)
            ?? throw new GameActionException("Combatant not found.", isActionRejected: true);

        if (combatantRef.CombatantType != "monster")
            throw new GameActionException("Can only plan actions for monster combatants.", isActionRejected: true);

        if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, combatantRef.Id))
            throw new GameActionException("That combatant is defeated.", isActionRejected: true);

        await ValidateActionAsync(action, combatantRef.Id, activeEncounter, isMonster: true);

        await redisState.SetPlannedActionAsync(activeEncounter.EncounterId, combatantRef.Id, action);

        return new PlanEnemyActionResult(gameSession.Id, combatantRef.Id, action.ActionType);
    }

    public async Task<LockInEnemiesResult> LockInEnemiesAsync(string joinCode, string callerUserId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var member = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == callerUserId);

        if (member is null || member.Role != Domain.Entities.CampaignRole.Gm)
            throw new GameActionException("Only the GM can lock in enemy actions.", isActionRejected: true);

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        // Seed default defend for any living monster with no planned action
        foreach (var c in activeEncounter.Combatants.Where(c => c.CombatantType == "monster"))
        {
            if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, c.Id)) continue;
            var existing = await redisState.GetPlannedActionAsync(activeEncounter.EncounterId, c.Id);
            if (existing is null)
                await redisState.SetPlannedActionAsync(activeEncounter.EncounterId, c.Id,
                    new PlannedAction("defend", null, null, null));
        }

        await redisState.AddLockedInAsync(activeEncounter.EncounterId, "gm_enemies");

        var allLockedIn = await AreAllLockedInAsync(activeEncounter, activeEncounter.EncounterId);
        if (allLockedIn)
        {
            var updated = activeEncounter with { RoundPhase = "resolving" };
            await redisState.SetActiveEncounterAsync(sessionId, updated);
        }

        return new LockInEnemiesResult(gameSession.Id, allLockedIn, activeEncounter.EncounterId);
    }

    private async Task<bool> AreAllLockedInAsync(ActiveEncounterEntry enc, string encounterId)
    {
        var lockedInSet = await redisState.GetLockedInSetAsync(encounterId);
        bool gmLockedIn = lockedInSet.Contains("gm_enemies");

        int livingPlayers = 0, lockedInPlayers = 0;
        foreach (var c in enc.Combatants.Where(c => c.CombatantType == "player"))
        {
            if (await redisState.IsDefeatedAsync(encounterId, c.Id)) continue;
            livingPlayers++;
            if (lockedInSet.Contains(c.Id)) lockedInPlayers++;
        }
        return gmLockedIn && lockedInPlayers >= livingPlayers;
    }

    private async Task ValidateActionAsync(PlannedAction action, string combatantId, ActiveEncounterEntry activeEncounter, bool isMonster = false)
    {
        switch (action.ActionType)
        {
            case "defend":
                break;

            case "move":
                if (action.TargetX is null || action.TargetY is null)
                    throw new GameActionException("Move requires target coordinates.", isActionRejected: true);

                var map = await redisState.GetMapAsync(activeEncounter.EncounterId)
                    ?? throw new GameActionException("Map data not found.", isActionRejected: true);

                if (action.TargetX < 0 || action.TargetX >= map.GridWidth || action.TargetY < 0 || action.TargetY >= map.GridHeight)
                    throw new GameActionException("Target position is out of bounds.", isActionRejected: true);

                // Monsters have no movement range limit — the GM controls them freely
                if (!isMonster && map.Positions.TryGetValue(combatantId, out var current))
                {
                    var chebyshev = Math.Max(Math.Abs(action.TargetX.Value - current.X), Math.Abs(action.TargetY.Value - current.Y));
                    if (chebyshev > 6)
                        throw new GameActionException("Move exceeds maximum range (6 squares).", isActionRejected: true);
                }
                break;

            case "attack":
                if (string.IsNullOrEmpty(action.TargetCombatantId))
                    throw new GameActionException("Attack requires a target.", isActionRejected: true);

                if (action.TargetCombatantId == combatantId)
                    throw new GameActionException("Cannot attack yourself.", isActionRejected: true);

                var _ = activeEncounter.Combatants.FirstOrDefault(c => c.Id == action.TargetCombatantId)
                    ?? throw new GameActionException("Target not found.", isActionRejected: true);

                if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, action.TargetCombatantId))
                    throw new GameActionException("Target is already defeated.", isActionRejected: true);
                break;

            default:
                throw new GameActionException($"Unknown action type: {action.ActionType}", isActionRejected: true);
        }
    }

    public async Task<ForceResolveResult> ForceResolveAsync(string joinCode, string userId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var member = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == userId);

        if (member is null || member.Role != Domain.Entities.CampaignRole.Gm)
            throw new GameActionException("Only the GM can force resolve.");

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        var updated = activeEncounter with { RoundPhase = "resolving" };
        await redisState.SetActiveEncounterAsync(sessionId, updated);

        return new ForceResolveResult(gameSession.Id, activeEncounter.EncounterId);
    }
}
