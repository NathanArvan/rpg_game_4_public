using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using RpgGame.Rules;
using System.Text.Json;

namespace RpgGame.Api.Services;

/// <summary>
/// Adapter between persistence (EF + Redis) and the pure rules engine. Loads the round's state,
/// maps it into <see cref="EncounterState"/> and plans, runs <see cref="RoundResolver"/>, maps the
/// <see cref="RoundOutcome"/> back to hub contracts, and persists the results.
/// No game rules live here — only data seeding defaults, mapping, feed text and I/O.
/// </summary>
public class ResolutionService(
    AppDbContext db,
    IRedisGameState redisState,
    IDiceRoller dice,
    ILogger<ResolutionService> logger) : IResolutionService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ResolutionPayload> ResolveRoundAsync(Guid encounterId)
    {
        logger.LogInformation("ResolveRound: START for encounter={EncounterId}", encounterId);

        // ----- Load -----

        var loaded = await EncounterStateLoader.LoadAsync(db, redisState, encounterId);
        var encounter = loaded.Encounter;
        var mapEntry = loaded.Map;
        var records = loaded.Records;
        logger.LogInformation("ResolveRound: loaded {CombatantCount} combatants", records.Count);

        var sessionIdStr = encounter.GameSessionId.ToString();
        var encounterIdStr = encounterId.ToString();

        var plannedActionsJson = await redisState.GetAllPlannedActionsAsync(encounterIdStr);
        logger.LogInformation("ResolveRound: loaded {ActionCount} planned actions from Redis", plannedActionsJson.Count);

        var plans = MapPlans(plannedActionsJson, records);

        // ----- Resolve (pure) -----

        var state = loaded.ToRulesState();
        var outcome = RoundResolver.Resolve(state, plans, dice);
        logger.LogInformation("ResolveRound: {TickCount} tick(s) resolved, {DefeatedCount} defeated this round",
            outcome.Ticks.Count, outcome.DefeatedThisRound.Count);

        var finalStates = outcome.Next.Combatants.ToDictionary(c => c.Id);
        var tickResults = outcome.Ticks.Select(t => ToTickResult(t, records)).ToList();

        // ----- Build payload -----

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr);
        var newRoundNumber = (activeEncounter?.RoundNumber ?? 1) + 1;

        var updatedCombatants = encounter.Combatants.Select(c =>
        {
            var id = c.Id.ToString();
            var final = finalStates[id];
            return CombatantDtos.Planning(c, final.Hp, final.IsDefeated);
        }).ToList();

        // ----- Persist -----

        // Write GameEvents to DB BEFORE Redis mutations
        var events = BuildGameEvents(encounterId, tickResults);
        logger.LogInformation("ResolveRound: writing {EventCount} GameEvents to DB", events.Count);
        if (events.Count > 0)
        {
            db.GameEvents.AddRange(events);
            await db.SaveChangesAsync();
        }
        logger.LogInformation("ResolveRound: DB write complete");

        foreach (var (combatantId, record) in records)
        {
            var newHp = finalStates[combatantId].Hp;
            if (newHp != record.StartingHp)
                await redisState.SetHpAsync(record.HpKey, newHp);
        }

        foreach (var combatantId in outcome.DefeatedThisRound)
            await redisState.MarkDefeatedAsync(encounterIdStr, combatantId);

        // Start from the stored map so entries the engine doesn't know about are kept as-is.
        var positions = new Dictionary<string, Position>(mapEntry.Positions);
        foreach (var final in outcome.Next.Combatants)
            if (final.Position is { } p)
                positions[final.Id] = new Position(p.X, p.Y);

        await redisState.SetMapAsync(encounterIdStr, new MapEntry(
            mapEntry.GridWidth, mapEntry.GridHeight,
            positions, mapEntry.FogOfWar));

        await redisState.ClearPlannedActionsAsync(encounterIdStr);
        await redisState.ClearLockedInAsync(encounterIdStr);

        // Transition to between_rounds — Hub will transition to planning after the broadcast delay
        if (activeEncounter is not null)
        {
            var betweenRounds = activeEncounter with { RoundPhase = "between_rounds", RoundNumber = newRoundNumber };
            await redisState.SetActiveEncounterAsync(sessionIdStr, betweenRounds);
        }

        logger.LogInformation("ResolveRound: COMPLETE — newRound={NewRound}, defeated={DefeatedIds}",
            newRoundNumber, string.Join(", ", outcome.DefeatedThisRound));
        return new ResolutionPayload(tickResults, newRoundNumber, outcome.DefeatedThisRound.ToList(), updatedCombatants);
    }

    // ----- Inbound mapping -----

    /// <summary>
    /// Deserializes Redis plans, preserving Redis order (the engine treats plan order as meaningful).
    /// Plans for ids that aren't combatants in this encounter are dropped; PlanningService never
    /// stores those. Unrecognized action types are dropped (PlanningService rejects them too).
    /// </summary>
    private Dictionary<string, IReadOnlyList<PlannedAction>> MapPlans(
        Dictionary<string, string> plannedActionsJson, Dictionary<string, CombatantRecord> records)
    {
        var plans = new Dictionary<string, IReadOnlyList<PlannedAction>>();
        foreach (var (combatantId, actionJson) in plannedActionsJson)
        {
            logger.LogDebug("ResolveRound: combatant={CombatantId} action={ActionJson}", combatantId, actionJson);
            var array = JsonSerializer.Deserialize<PlannedActionArray>(actionJson, _jsonOptions);
            if (array is null) continue;

            if (!records.ContainsKey(combatantId))
            {
                logger.LogWarning("ResolveRound: ignoring plan for unknown combatant {CombatantId}", combatantId);
                continue;
            }

            var queue = new List<PlannedAction>();
            foreach (var entry in array.Actions)
            {
                if (RulesWireMapping.ParseActionType(entry.Type) is not { } type) continue;
                queue.Add(new PlannedAction(
                    type,
                    entry.TargetCell is null ? null : new GridPosition(entry.TargetCell.X, entry.TargetCell.Y),
                    entry.TargetCombatantId));
            }
            plans[combatantId] = queue;
        }
        return plans;
    }

    // ----- Outbound mapping -----

    private static TickResult ToTickResult(TickOutcome tick, Dictionary<string, CombatantRecord> records)
    {
        string NameOf(string id) => records.TryGetValue(id, out var r) ? r.State.Name : "Unknown";

        var movements = tick.Movements.Select(m => new MovementResult(
            m.CombatantId,
            NameOf(m.CombatantId),
            m.Path.Select(p => new PositionDto(p.X, p.Y)).ToList(),
            new PositionDto(m.FinalPosition.X, m.FinalPosition.Y),
            m.WasBlocked,
            m.BlockedById)).ToList();

        var attacks = tick.Attacks.Select(a =>
        {
            var attackerName = NameOf(a.AttackerId);
            var targetName = NameOf(a.TargetId);
            return new AttackResult(
                a.AttackerId, attackerName,
                a.TargetId, targetName,
                RulesWireMapping.ToWire(a.AttackType), a.Hit, a.Damage,
                a.TargetHpAfter, a.TargetDefeated, a.Wasted,
                ResolutionFeedFormatter.Attack(a, attackerName, targetName));
        }).ToList();

        var effects = tick.Effects.Select(e => new EffectResult(
            e.CombatantId,
            RulesWireMapping.ToWire(e.Type),
            ResolutionFeedFormatter.Effect(e, NameOf(e.CombatantId)))).ToList();

        return new TickResult(tick.Index, movements, attacks, effects);
    }

    private static List<GameEvent> BuildGameEvents(Guid encounterId, List<TickResult> tickResults)
    {
        var events = new List<GameEvent>();
        var now = DateTimeOffset.UtcNow;

        foreach (var tick in tickResults)
        {
            foreach (var movement in tick.Movements)
            {
                events.Add(new GameEvent
                {
                    Id = Guid.NewGuid(),
                    EncounterId = encounterId,
                    OccurredAt = now,
                    EventType = "movement",
                    Payload = JsonSerializer.Serialize(new { movement.CombatantId, movement.FinalPosition, movement.WasBlocked }, _jsonOptions),
                });
            }
            foreach (var attack in tick.Attacks)
            {
                var eventType = attack.Wasted ? "attack_wasted" : (attack.Hit ? "attack_hit" : "attack_miss");
                events.Add(new GameEvent
                {
                    Id = Guid.NewGuid(),
                    EncounterId = encounterId,
                    OccurredAt = now,
                    EventType = eventType,
                    Payload = JsonSerializer.Serialize(new { attack.AttackerId, attack.TargetId, attack.Damage, attack.TargetNewHp }, _jsonOptions),
                });
            }
        }

        return events;
    }
}
