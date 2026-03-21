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

        // Validate action
        switch (action.ActionType)
        {
            case "move":
                if (action.TargetX is null || action.TargetY is null)
                    throw new GameActionException("Move requires target coordinates.", isActionRejected: true);

                var map = await redisState.GetMapAsync(activeEncounter.EncounterId)
                    ?? throw new GameActionException("Map data not found.", isActionRejected: true);

                if (action.TargetX < 0 || action.TargetX >= map.GridWidth || action.TargetY < 0 || action.TargetY >= map.GridHeight)
                    throw new GameActionException("Target position is out of bounds.", isActionRejected: true);

                if (map.Positions.TryGetValue(combatantRef.Id, out var current))
                {
                    var chebyshev = Math.Max(Math.Abs(action.TargetX.Value - current.X), Math.Abs(action.TargetY.Value - current.Y));
                    if (chebyshev > 6)
                        throw new GameActionException("Move exceeds maximum range (6 squares).", isActionRejected: true);
                }
                break;

            case "attack":
                if (string.IsNullOrEmpty(action.TargetCombatantId))
                    throw new GameActionException("Attack requires a target.", isActionRejected: true);

                if (action.TargetCombatantId == combatantRef.Id)
                    throw new GameActionException("You cannot attack yourself.", isActionRejected: true);

                var targetRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == action.TargetCombatantId)
                    ?? throw new GameActionException("Target not found.", isActionRejected: true);

                if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, action.TargetCombatantId))
                    throw new GameActionException("Target is already defeated.", isActionRejected: true);
                break;

            default:
                throw new GameActionException($"Unknown action type: {action.ActionType}", isActionRejected: true);
        }

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

        // Check if all living combatants are locked in
        var lockedInCount = await redisState.GetLockedInCountAsync(activeEncounter.EncounterId);
        var livingCount = 0;
        foreach (var c in activeEncounter.Combatants)
        {
            if (!await redisState.IsDefeatedAsync(activeEncounter.EncounterId, c.Id))
                livingCount++;
        }

        var allLockedIn = lockedInCount >= livingCount;
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
