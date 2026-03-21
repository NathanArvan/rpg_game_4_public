using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using System.Text.Json;

namespace RpgGame.Api.Services;

public class ResolutionService(AppDbContext db, IRedisGameState redisState, ILogger<ResolutionService> logger) : IResolutionService
{
    private const int PlanningDurationSeconds = 60;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<ResolutionResult> ResolveRoundAsync(string sessionId, string encounterId)
    {
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.");

        var plannedActions = await redisState.GetAllPlannedActionsAsync(encounterId);
        var map = await redisState.GetMapAsync(encounterId)
            ?? throw new GameActionException("Map data not found.");

        // Build lookup of combatant refs by id
        var combatantLookup = activeEncounter.Combatants.ToDictionary(c => c.Id);

        // Load live state for each combatant
        var liveState = new Dictionary<string, (int currentHp, int attackBonus, int damageBase, int dodgeChance, int maxAp, int movementRange)>();
        foreach (var c in activeEncounter.Combatants)
        {
            liveState[c.Id] = await redisState.GetCharacterLiveStateAsync(c.CharacterId);
        }

        // Track defeated set
        var defeatedSet = new HashSet<string>();
        foreach (var c in activeEncounter.Combatants)
        {
            if (await redisState.IsDefeatedAsync(encounterId, c.Id))
                defeatedSet.Add(c.Id);
        }

        var steps = new List<ResolutionStepDto>();
        var positions = new Dictionary<string, Position>(map.Positions);

        // --- MOVEMENT PHASE ---
        var moveActions = plannedActions
            .Where(kv => kv.Value.ActionType == "move" && !defeatedSet.Contains(kv.Key))
            .ToList();

        // Group moves by target cell to detect conflicts
        var movesByTarget = moveActions
            .Where(kv => kv.Value.TargetX is not null && kv.Value.TargetY is not null)
            .GroupBy(kv => (kv.Value.TargetX!.Value, kv.Value.TargetY!.Value))
            .ToList();

        var movedCombatants = new HashSet<string>();

        foreach (var group in movesByTarget)
        {
            var (targetX, targetY) = group.Key;
            var contenders = group.ToList();

            if (contenders.Count == 1)
            {
                // No conflict — move succeeds
                var (combatantId, action) = contenders[0];
                var combatant = combatantLookup[combatantId];
                positions[combatantId] = new Position(targetX, targetY);
                movedCombatants.Add(combatantId);

                steps.Add(new ResolutionStepDto(
                    "movement", combatantId, combatant.CharacterName, "move",
                    null, null, null, null, null, targetX, targetY,
                    false, $"{combatant.CharacterName} moves to ({targetX}, {targetY})."));
            }
            else
            {
                // Conflict — Dex tiebreaker
                var charIds = contenders
                    .Select(c => GuidParser.TryParse(combatantLookup[c.Key].CharacterId))
                    .Where(id => id is not null)
                    .Select(id => id!.Value)
                    .ToList();

                var characters = await db.Characters
                    .Where(c => charIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id);

                // Sort by Speed descending (tiebreaker for movement), then random
                var sorted = contenders
                    .OrderByDescending(c =>
                    {
                        var charId = GuidParser.TryParse(combatantLookup[c.Key].CharacterId);
                        if (charId is not null && characters.TryGetValue(charId.Value, out var ch))
                            return ch.BaseStats.Speed;
                        return 0;
                    })
                    .ThenBy(_ => Random.Shared.Next())
                    .ToList();

                // Winner moves
                var (winnerId, winnerAction) = sorted[0];
                var winner = combatantLookup[winnerId];
                positions[winnerId] = new Position(targetX, targetY);
                movedCombatants.Add(winnerId);

                steps.Add(new ResolutionStepDto(
                    "movement", winnerId, winner.CharacterName, "move",
                    null, null, null, null, null, targetX, targetY,
                    false, $"{winner.CharacterName} wins the position at ({targetX}, {targetY})."));

                // Losers stay
                for (int i = 1; i < sorted.Count; i++)
                {
                    var (loserId, _) = sorted[i];
                    var loser = combatantLookup[loserId];
                    var loserPos = positions.GetValueOrDefault(loserId);
                    steps.Add(new ResolutionStepDto(
                        "movement", loserId, loser.CharacterName, "move_blocked",
                        null, null, null, null, null, loserPos?.X, loserPos?.Y,
                        false, $"{loser.CharacterName} is blocked from moving to ({targetX}, {targetY})."));
                }
            }
        }

        // --- ATTACK PHASE ---
        var attackActions = plannedActions
            .Where(kv => kv.Value.ActionType == "attack" && !defeatedSet.Contains(kv.Key))
            .ToList();

        // Compute all damage simultaneously before applying
        var damageResults = new List<(string attackerId, string targetId, int damage, string damageDice, string attackerName, string targetName)>();

        foreach (var (combatantId, action) in attackActions)
        {
            if (string.IsNullOrEmpty(action.TargetCombatantId)) continue;
            if (!combatantLookup.TryGetValue(combatantId, out var attacker)) continue;
            if (!combatantLookup.TryGetValue(action.TargetCombatantId, out var target)) continue;

            var (_, _, attackerDamageBase, _, _, _) = liveState.GetValueOrDefault(combatantId);
            var dmgBase = attackerDamageBase > 0 ? attackerDamageBase : 2;
            var damage = CombatDice.RollDamage(dmgBase);
            var damageDice = CombatDice.DamageDiceString(dmgBase);

            damageResults.Add((combatantId, action.TargetCombatantId, damage, damageDice, attacker.CharacterName, target.CharacterName));
        }

        // Apply all damage simultaneously
        var hpDeltas = new Dictionary<string, int>(); // combatantId -> total damage taken
        foreach (var (_, targetId, damage, _, _, _) in damageResults)
        {
            hpDeltas.TryGetValue(targetId, out var existing);
            hpDeltas[targetId] = existing + damage;
        }

        // Apply deltas and generate steps
        var newHpValues = new Dictionary<string, int>(); // combatantId -> new HP
        foreach (var (targetId, totalDamage) in hpDeltas)
        {
            var combatant = combatantLookup[targetId];
            var (currentHp, _, _, _, _, _) = liveState.GetValueOrDefault(targetId);
            var newHp = Math.Max(0, currentHp - totalDamage);
            newHpValues[targetId] = newHp;
        }

        foreach (var (attackerId, targetId, damage, damageDice, attackerName, targetName) in damageResults)
        {
            var newHp = newHpValues.GetValueOrDefault(targetId);
            var defeated = newHp <= 0 && !defeatedSet.Contains(targetId);

            var feedText = defeated
                ? $"{attackerName} attacks {targetName} for {damage} ({damageDice}) damage! {targetName} is defeated!"
                : $"{attackerName} attacks {targetName} for {damage} ({damageDice}) damage. ({newHp} HP remaining)";

            steps.Add(new ResolutionStepDto(
                "attack", attackerId, attackerName, "attack",
                targetId, targetName, damage, damageDice, newHp,
                null, null, defeated, feedText));
        }

        // Mark newly defeated
        foreach (var (targetId, newHp) in newHpValues)
        {
            if (newHp <= 0 && !defeatedSet.Contains(targetId))
                defeatedSet.Add(targetId);
        }

        // --- WRITE TO REDIS ---
        // Update map positions
        var updatedMapEntry = map with { Positions = positions };
        await redisState.SetMapAsync(encounterId, updatedMapEntry);

        // Set HP values and mark defeated
        foreach (var (targetId, newHp) in newHpValues)
        {
            var combatant = combatantLookup[targetId];
            await redisState.SetHpAsync(combatant.CharacterId, newHp);
            if (newHp <= 0)
                await redisState.MarkDefeatedAsync(encounterId, targetId);
        }

        // Clear planned actions and locked in
        await redisState.ClearPlannedActionsAsync(encounterId);
        await redisState.ClearLockedInAsync(encounterId);

        // Build updated positions DTO
        var updatedPositions = positions.ToDictionary(
            kv => kv.Key,
            kv => new PositionDto(kv.Value.X, kv.Value.Y));

        // Check encounter end — if ≤1 living combatant remains
        var livingCount = activeEncounter.Combatants.Count(c => !defeatedSet.Contains(c.Id));
        var encounterEnded = livingCount <= 1;

        var encounterGuid = GuidParser.ParseOrThrow(encounterId, "encounter ID");

        if (encounterEnded)
        {
            var encounter = await db.Encounters.FindAsync(encounterGuid);
            if (encounter is not null)
            {
                encounter.Status = EncounterStatus.Ended;
                await db.SaveChangesAsync();
            }
            await redisState.DeleteActiveEncounterAsync(sessionId);

            logger.LogInformation("Encounter {EncounterId} ended after resolution.", encounterId);
        }

        // Persist GameEvents
        var now = DateTimeOffset.UtcNow;
        foreach (var step in steps)
        {
            db.GameEvents.Add(new GameEvent
            {
                Id = Guid.NewGuid(),
                EncounterId = encounterGuid,
                OccurredAt = now,
                EventType = $"resolution_{step.Phase}",
                Payload = JsonSerializer.Serialize(step, _jsonOptions),
            });
        }
        await db.SaveChangesAsync();

        var resolutionPayload = new ResolutionCompletedPayload(
            encounterId,
            activeEncounter.RoundNumber,
            steps,
            updatedPositions,
            encounterEnded);

        PlanningPhaseStartedPayload? nextRoundPayload = null;

        if (!encounterEnded)
        {
            // Start next planning round
            var newRoundNumber = activeEncounter.RoundNumber + 1;
            var planningExpiresAt = DateTimeOffset.UtcNow.AddSeconds(PlanningDurationSeconds).ToUnixTimeMilliseconds();

            var updatedEncounter = activeEncounter with
            {
                RoundPhase = "planning",
                RoundNumber = newRoundNumber,
                PlanningTimerExpiresAt = planningExpiresAt,
            };
            await redisState.SetActiveEncounterAsync(sessionId, updatedEncounter);

            // Update DB round number
            var encounter = await db.Encounters.FindAsync(encounterGuid);
            if (encounter is not null)
            {
                encounter.RoundNumber = newRoundNumber;
                await db.SaveChangesAsync();
            }

            nextRoundPayload = new PlanningPhaseStartedPayload(encounterId, newRoundNumber, planningExpiresAt);
        }

        var sessionGuid = Guid.TryParse(sessionId, out var sid) ? sid : Guid.Empty;
        return new ResolutionResult(sessionGuid, resolutionPayload, nextRoundPayload);
    }
}
