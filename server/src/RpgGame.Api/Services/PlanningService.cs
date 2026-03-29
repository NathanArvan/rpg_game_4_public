using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using System.Text.Json;

namespace RpgGame.Api.Services;

public class PlanningService(AppDbContext db, IRedisGameState redisState) : IPlanningService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PlanActionResult> PlanActionAsync(string joinCode, string userId, PlannedActionArray action)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var sessionIdStr = gameSession.Id.ToString();
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        var combatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.UserId == userId)
            ?? throw new GameActionException("You are not in this encounter.", isActionRejected: true);

        if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, combatantRef.Id))
            throw new GameActionException("You are defeated.", isActionRejected: true);

        if (action.Actions.Count != 1)
            throw new GameActionException("Action array must contain exactly 1 action.", isActionRejected: true);

        var entry = action.Actions[0];
        await ValidateActionEntry(entry, combatantRef.Id, activeEncounter);

        var actionJson = JsonSerializer.Serialize(action, _jsonOptions);
        await redisState.SetPlannedActionAsync(activeEncounter.EncounterId, combatantRef.Id, actionJson);

        return new PlanActionResult(true);
    }

    public async Task<LockInResult> LockInAsync(string joinCode, string userId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var sessionIdStr = gameSession.Id.ToString();
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        var combatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.UserId == userId)
            ?? throw new GameActionException("You are not in this encounter.", isActionRejected: true);

        if (!await redisState.HasPlannedActionAsync(activeEncounter.EncounterId, combatantRef.Id))
            throw new GameActionException("You must plan an action before locking in.", isActionRejected: true);

        await redisState.AddLockedInAsync(activeEncounter.EncounterId, combatantRef.Id);

        var encounterId = activeEncounter.EncounterId;
        var allLockedIn = await AreAllLivingCombatantsLockedIn(activeEncounter);
        var combatantName = await GetCombatantCharacterName(encounterId, combatantRef.Id);

        return new LockInResult(gameSession.Id, combatantRef.Id, combatantName, allLockedIn, encounterId);
    }

    public async Task<ForceResolveResult> ForceResolveAsync(string joinCode, string gmUserId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var member = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == gmUserId);

        if (member is null || member.Role != CampaignRole.Gm)
            throw new GameActionException("Only the GM can force resolve.", isActionRejected: true);

        var sessionIdStr = gameSession.Id.ToString();
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        var encounterId = activeEncounter.EncounterId;
        var defendAction = JsonSerializer.Serialize(
            new PlannedActionArray(new List<PlannedActionEntry> { new("defend", null, null) }),
            _jsonOptions);

        var missingCombatantIds = new List<string>();
        foreach (var combatant in activeEncounter.Combatants)
        {
            if (await redisState.IsDefeatedAsync(encounterId, combatant.Id)) continue;
            if (!await redisState.HasPlannedActionAsync(encounterId, combatant.Id))
                await redisState.SetPlannedActionAsync(encounterId, combatant.Id, defendAction);
            if (!await redisState.IsLockedInAsync(encounterId, combatant.Id))
                missingCombatantIds.Add(combatant.Id);
        }

        if (missingCombatantIds.Count > 0)
            await redisState.AddLockedInBatchAsync(encounterId, missingCombatantIds);

        return new ForceResolveResult(gameSession.Id, true, encounterId);
    }

    private async Task ValidateActionEntry(
        PlannedActionEntry entry,
        string combatantId,
        ActiveEncounterEntry activeEncounter)
    {
        switch (entry.Type)
        {
            case "move":
                if (entry.TargetCell is null)
                    throw new GameActionException("Move action requires a target cell.", isActionRejected: true);

                var x = entry.TargetCell.X;
                var y = entry.TargetCell.Y;

                if (x < 0 || x > 19 || y < 0 || y > 19)
                    throw new GameActionException("Target cell is out of bounds (0–19).", isActionRejected: true);

                var map = await redisState.GetMapAsync(activeEncounter.EncounterId)
                    ?? throw new GameActionException("Map data not found.", isActionRejected: true);

                if (!map.Positions.TryGetValue(combatantId, out var current))
                    throw new GameActionException("Current position not found.", isActionRejected: true);

                var chebyshev = Math.Max(Math.Abs(x - current.X), Math.Abs(y - current.Y));
                if (chebyshev > 6)
                    throw new GameActionException("Move exceeds maximum range (6 squares).", isActionRejected: true);
                break;

            case "melee_attack":
            case "ranged_attack":
                if (string.IsNullOrEmpty(entry.TargetCombatantId))
                    throw new GameActionException("Attack action requires a target combatant ID.", isActionRejected: true);

                if (entry.TargetCombatantId == combatantId)
                    throw new GameActionException("You cannot target yourself.", isActionRejected: true);

                var targetRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == entry.TargetCombatantId)
                    ?? throw new GameActionException("Target combatant not found.", isActionRejected: true);

                if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, entry.TargetCombatantId))
                    throw new GameActionException("Target is already defeated.", isActionRejected: true);
                break;

            case "defend":
                break;

            default:
                throw new GameActionException($"Unknown action type: {entry.Type}", isActionRejected: true);
        }
    }

    private async Task<bool> AreAllLivingCombatantsLockedIn(ActiveEncounterEntry activeEncounter)
    {
        var encounterId = activeEncounter.EncounterId;
        foreach (var combatant in activeEncounter.Combatants)
        {
            if (await redisState.IsDefeatedAsync(encounterId, combatant.Id)) continue;
            if (!await redisState.IsLockedInAsync(encounterId, combatant.Id)) return false;
        }
        return true;
    }

    private async Task<string> GetCombatantCharacterName(string encounterId, string combatantId)
    {
        var combatantGuid = GuidParser.TryParse(combatantId);
        var encounterGuid = GuidParser.TryParse(encounterId);
        if (combatantGuid is null || encounterGuid is null) return "Unknown";

        var dbCombatant = await db.EncounterCombatants
            .FirstOrDefaultAsync(c => c.Id == combatantGuid.Value && c.EncounterId == encounterGuid.Value);

        return dbCombatant?.CharacterName ?? "Unknown";
    }
}
