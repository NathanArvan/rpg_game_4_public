using Microsoft.EntityFrameworkCore;
using RpgGame.Api.CombatProfiles;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using System.Text.Json;

namespace RpgGame.Api.Services;

public class PlanningService(AppDbContext db, IRedisGameState redisState, ILogger<PlanningService> logger) : IPlanningService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PlanActionResult> PlanActionAsync(string joinCode, string userId, PlannedActionArray action, string? combatantId = null)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var sessionIdStr = gameSession.Id.ToString();
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        var combatantRef = ResolveOwnedCombatant(activeEncounter, userId, combatantId);

        if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, combatantRef.Id))
            throw new GameActionException("You are defeated.", isActionRejected: true);

        if (action.Actions.Count != 1)
            throw new GameActionException("Action array must contain exactly 1 action.", isActionRejected: true);

        var entry = action.Actions[0];
        await ValidateActionEntry(entry, combatantRef.Id, activeEncounter);

        var actionJson = JsonSerializer.Serialize(action, _jsonOptions);
        await redisState.SetPlannedActionAsync(activeEncounter.EncounterId, combatantRef.Id, actionJson);

        logger.LogInformation("PlanAction: user={UserId} combatant={CombatantId} action={ActionType}",
            userId, combatantRef.Id, entry.Type);
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

        var encounterId = activeEncounter.EncounterId;
        var owned = OwnedCombatants(activeEncounter, userId);
        if (owned.Count == 0)
            throw new GameActionException("You are not in this encounter.", isActionRejected: true);

        // Lock in every living character you control. At least one must have a plan; the rest defend.
        var living = new List<CombatantRef>();
        foreach (var c in owned)
            if (!await redisState.IsDefeatedAsync(encounterId, c.Id)) living.Add(c);

        var unplanned = new List<CombatantRef>();
        foreach (var c in living)
            if (!await redisState.HasPlannedActionAsync(encounterId, c.Id)) unplanned.Add(c);

        if (living.Count == 0 || unplanned.Count == living.Count)
            throw new GameActionException("You must plan an action before locking in.", isActionRejected: true);

        var defendJson = JsonSerializer.Serialize(
            new PlannedActionArray(new List<PlannedActionEntry> { new("defend", null, null) }), _jsonOptions);
        foreach (var c in unplanned)
            await redisState.SetPlannedActionAsync(encounterId, c.Id, defendJson);

        if (living.Count == 1)
            await redisState.AddLockedInAsync(encounterId, living[0].Id);
        else
            await redisState.AddLockedInBatchAsync(encounterId, living.Select(c => c.Id));

        var allLockedIn = await AreAllLivingCombatantsLockedIn(activeEncounter);
        var names = new List<string>();
        foreach (var c in living)
            names.Add(await GetCombatantCharacterName(encounterId, c.Id));

        logger.LogInformation("LockIn: user={UserId} combatants={Names} allLockedIn={AllLockedIn} totalCombatants={Total}",
            userId, string.Join(", ", names), allLockedIn, activeEncounter.Combatants.Count);
        return new LockInResult(gameSession.Id, living[0].Id, names[0], allLockedIn, encounterId, names);
    }

    /// <summary>The player characters this user controls (several in Quick Battle, one otherwise).</summary>
    private static List<CombatantRef> OwnedCombatants(ActiveEncounterEntry encounter, string userId) =>
        encounter.Combatants.Where(c => c.UserId == userId && c.CombatantType == "player").ToList();

    private static CombatantRef ResolveOwnedCombatant(ActiveEncounterEntry encounter, string userId, string? combatantId)
    {
        var owned = OwnedCombatants(encounter, userId);
        if (owned.Count == 0)
            throw new GameActionException("You are not in this encounter.", isActionRejected: true);

        if (combatantId is null)
        {
            if (owned.Count > 1)
                throw new GameActionException("You control several characters; say which one this action is for.", isActionRejected: true);
            return owned[0];
        }

        return owned.FirstOrDefault(c => c.Id == combatantId)
            ?? throw new GameActionException("You don't control that combatant.", isActionRejected: true);
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

                var map = await redisState.GetMapAsync(activeEncounter.EncounterId)
                    ?? throw new GameActionException("Map data not found.", isActionRejected: true);

                if (x < 0 || x >= map.GridWidth || y < 0 || y >= map.GridHeight)
                    throw new GameActionException(
                        $"Target cell is out of bounds (0–{map.GridWidth - 1}, 0–{map.GridHeight - 1}).", isActionRejected: true);

                if (!map.Positions.TryGetValue(combatantId, out var current))
                    throw new GameActionException("Current position not found.", isActionRejected: true);

                // Movement range comes from the combatant's combat profile (3B.1).
                var combatantGuid = GuidParser.TryParse(combatantId);
                var profile = combatantGuid is null
                    ? null
                    : await db.EncounterCombatants
                        .AsNoTracking()
                        .Where(c => c.Id == combatantGuid.Value)
                        .Select(c => c.Profile)
                        .FirstOrDefaultAsync();
                var range = CombatProfileMapping.MovementRange(profile);

                var chebyshev = Math.Max(Math.Abs(x - current.X), Math.Abs(y - current.Y));
                if (chebyshev > range)
                    throw new GameActionException($"Move exceeds maximum range ({range} squares).", isActionRejected: true);
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

    public async Task<PlanActionResult> PlanEnemyActionAsync(string joinCode, string gmUserId, string combatantId, PlannedActionArray action)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var member = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == gmUserId);

        if (member is null || member.Role != CampaignRole.Gm)
            throw new GameActionException("Only the GM can plan enemy actions.", isActionRejected: true);

        logger.LogInformation("PlanEnemyAction: GM validated, encounterId lookup for session={SessionId}", gameSession.Id);

        var sessionIdStr = gameSession.Id.ToString();
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        logger.LogInformation("PlanEnemyAction: phase={Phase} encounterId={EncounterId} combatantCount={Count}",
            activeEncounter.RoundPhase, activeEncounter.EncounterId, activeEncounter.Combatants.Count);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        var combatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == combatantId);
        if (combatantRef is null)
        {
            logger.LogWarning("PlanEnemyAction: combatant {CombatantId} not found in encounter. Known ids: {KnownIds}",
                combatantId, string.Join(", ", activeEncounter.Combatants.Select(c => c.Id)));
            throw new GameActionException("Combatant not found.", isActionRejected: true);
        }

        logger.LogInformation("PlanEnemyAction: combatant found, type={CombatantType}", combatantRef.CombatantType);

        if (combatantRef.CombatantType != "monster")
            throw new GameActionException("This method is only for monster combatants.", isActionRejected: true);

        if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, combatantId))
            throw new GameActionException("That combatant is defeated.", isActionRejected: true);

        if (action.Actions.Count != 1)
            throw new GameActionException("Action array must contain exactly 1 action.", isActionRejected: true);

        var entry = action.Actions[0];
        logger.LogInformation("PlanEnemyAction: validating action type={Type}", entry.Type);
        await ValidateActionEntry(entry, combatantId, activeEncounter);

        var actionJson = JsonSerializer.Serialize(action, _jsonOptions);
        await redisState.SetPlannedActionAsync(activeEncounter.EncounterId, combatantId, actionJson);

        logger.LogInformation("PlanEnemyAction: gm={GmUserId} combatant={CombatantId} action={ActionType} — stored",
            gmUserId, combatantId, entry.Type);
        return new PlanActionResult(true);
    }

    public async Task<LockInResult> LockInEnemiesAsync(string joinCode, string gmUserId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var member = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == gmUserId);

        if (member is null || member.Role != CampaignRole.Gm)
            throw new GameActionException("Only the GM can lock in enemies.", isActionRejected: true);

        var sessionIdStr = gameSession.Id.ToString();
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Not in planning phase.", isActionRejected: true);

        var encounterId = activeEncounter.EncounterId;
        var defendActionJson = JsonSerializer.Serialize(
            new PlannedActionArray(new List<PlannedActionEntry> { new("defend", null, null) }),
            _jsonOptions);

        var monsterIds = new List<string>();
        foreach (var combatant in activeEncounter.Combatants)
        {
            if (combatant.CombatantType != "monster") continue;
            if (await redisState.IsDefeatedAsync(encounterId, combatant.Id))
            {
                logger.LogInformation("LockInEnemies: skipping defeated monster {MonsterId}", combatant.Id);
                continue;
            }
            monsterIds.Add(combatant.Id);
            var hasAction = await redisState.HasPlannedActionAsync(encounterId, combatant.Id);
            logger.LogInformation("LockInEnemies: monster {MonsterId} — hasAction={HasAction}", combatant.Id, hasAction);
            if (!hasAction)
                await redisState.SetPlannedActionAsync(encounterId, combatant.Id, defendActionJson);
        }

        if (monsterIds.Count > 0)
            await redisState.AddLockedInBatchAsync(encounterId, monsterIds);

        var allLockedIn = await AreAllLivingCombatantsLockedIn(activeEncounter);

        logger.LogInformation("LockInEnemies: gm={GmUserId} monsters={MonsterCount} allLockedIn={AllLockedIn}",
            gmUserId, monsterIds.Count, allLockedIn);
        return new LockInResult(gameSession.Id, "", "GM", allLockedIn, encounterId);
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
