using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Conditions;
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

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr);
        var roundNumber = activeEncounter?.RoundNumber ?? 1;

        var state = loaded.ToRulesState();
        // Seeded test dice (Dice:Seed) start afresh each round, so a round's rolls don't depend on earlier ones.
        var roundDice = dice is RoundSeededDice seeded ? seeded.ForRound(roundNumber) : dice;
        var outcome = RoundResolver.Resolve(state, plans, roundDice);
        logger.LogInformation("ResolveRound: {TickCount} tick(s) resolved, {DefeatedCount} defeated this round",
            outcome.Ticks.Count, outcome.DefeatedThisRound.Count);

        var finalStates = outcome.Next.Combatants.ToDictionary(c => c.Id);
        var conditionLookup = ConditionLookup(records);
        var tickResults = outcome.Ticks.Select(t => ToTickResult(t, records, conditionLookup)).ToList();
        var finalConditions = finalStates.ToDictionary(kv => kv.Key, kv => ToStored(kv.Value.Conditions, conditionLookup));

        // ----- Build payload -----

        var newRoundNumber = roundNumber + 1;

        var updatedCombatants = encounter.Combatants.Select(c =>
        {
            var id = c.Id.ToString();
            var final = finalStates[id];
            return CombatantDtos.Planning(c, final.Hp, final.Status, final.BleedoutRoundsLeft, final.Cooldowns, finalConditions[id]);
        }).ToList();

        // ----- Persist -----

        // Write GameEvents to DB BEFORE Redis mutations
        var events = BuildGameEvents(encounterId, tickResults);
        events.AddRange(outcome.PeriodicEffects.Select(p => new GameEvent
        {
            Id = Guid.NewGuid(),
            EncounterId = encounterId,
            OccurredAt = DateTimeOffset.UtcNow,
            EventType = p.Kind == PeriodicKind.Heal ? "periodic_heal" : "periodic_damage",
            Payload = JsonSerializer.Serialize(new { p.CombatantId, p.ConditionId, p.Amount, NewHp = p.HpAfter }, _jsonOptions),
        }));
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

        // Healed back up (3B.3): out of the defeated set and the status hash, so they plan,
        // lock in and count as standing again from the next round.
        foreach (var combatantId in outcome.RevivedThisRound)
            await redisState.ReviveAsync(encounterIdStr, combatantId);

        // Statuses beyond standing (3B.5): write any that changed, including bleedout countdowns.
        foreach (var final in outcome.Next.Combatants)
        {
            var before = records[final.Id].State;
            if (final.Status == CombatantStatus.Standing) continue;
            if (final.Status == before.Status && final.BleedoutRoundsLeft == before.BleedoutRoundsLeft) continue;
            await redisState.SetStatusAsync(encounterIdStr, final.Id,
                new CombatantStatusEntry(RulesWireMapping.ToWire(final.Status), final.BleedoutRoundsLeft));
        }

        // Ability cooldowns (3B.3): write only the ones that changed.
        foreach (var final in outcome.Next.Combatants)
        {
            var before = records[final.Id].State.Cooldowns;
            bool same = before.Count == final.Cooldowns.Count
                && before.All(kv => final.Cooldowns.TryGetValue(kv.Key, out var v) && v == kv.Value);
            if (!same)
                await redisState.SetCooldownsAsync(encounterIdStr, final.Id, final.Cooldowns);
        }

        // Conditions (3B.4): write only the ones that changed.
        foreach (var final in outcome.Next.Combatants)
        {
            if (final.Conditions.SequenceEqual(records[final.Id].State.Conditions)) continue;
            await redisState.SetConditionsAsync(encounterIdStr, final.Id, finalConditions[final.Id]);
        }

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
        var endOfRound = outcome.EndOfRound.Select(b =>
        {
            var name = records.TryGetValue(b.CombatantId, out var r) ? r.State.Name : "Unknown";
            return new BleedoutResultDto(b.CombatantId, name, b.RoundsLeft, b.Died, ResolutionFeedFormatter.Bleedout(b, name));
        }).ToList();

        var periodic = outcome.PeriodicEffects.Select((p, i) =>
        {
            var name = records.TryGetValue(p.CombatantId, out var r) ? r.State.Name : "Unknown";
            var condition = conditionLookup.GetValueOrDefault(p.ConditionId);
            var conditionName = condition?.Name ?? p.ConditionId;
            bool last = i == outcome.PeriodicEffects.Count - 1 || outcome.PeriodicEffects[i + 1].CombatantId != p.CombatantId;
            return new PeriodicResultDto(p.CombatantId, name, p.ConditionId, conditionName, condition?.Harmful ?? false,
                condition?.Tags ?? [], p.Kind == PeriodicKind.Heal ? "heal" : "damage", p.Amount, p.ConditionDamage,
                p.HpAfter, RulesWireMapping.ToWire(p.Status), p.Fell,
                ResolutionFeedFormatter.Periodic(p, name, conditionName, last));
        }).ToList();

        var expired = outcome.ExpiredConditions.Select(x =>
        {
            var name = records.TryGetValue(x.CombatantId, out var r) ? r.State.Name : "Unknown";
            var conditionName = conditionLookup.TryGetValue(x.ConditionId, out var c) ? c.Name : x.ConditionId;
            return new ExpiredConditionDto(x.CombatantId, name, x.ConditionId, conditionName,
                ResolutionFeedFormatter.ConditionExpired(name, conditionName));
        }).ToList();

        return new ResolutionPayload(tickResults, newRoundNumber, outcome.DefeatedThisRound.ToList(), updatedCombatants,
            ToQueues(outcome.ResolvedPlans, records), endOfRound, outcome.RevivedThisRound.ToList(), expired, periodic);
    }

    // ----- Inbound mapping -----

    /// <summary>
    /// Deserializes Redis plans in combatant order (the engine treats plan order as meaningful).
    /// Plans for ids that aren't combatants in this encounter are dropped; PlanningService never
    /// stores those. Unrecognized action types are dropped (PlanningService rejects them too).
    /// </summary>
    private Dictionary<string, IReadOnlyList<PlannedAction>> MapPlans(
        Dictionary<string, string> plannedActionsJson, Dictionary<string, CombatantRecord> records)
    {
        foreach (var unknown in plannedActionsJson.Keys.Where(id => !records.ContainsKey(id)))
            logger.LogWarning("ResolveRound: ignoring plan for unknown combatant {CombatantId}", unknown);

        // Plan order decides roll order, result order and same-Speed contested cells, so it follows the
        // combatants' creation order (records come in SortOrder), not the Redis hash: a hash of long
        // values iterates in hash order of its keys (random ids), which made rolls unrepeatable.
        var plans = new Dictionary<string, IReadOnlyList<PlannedAction>>();
        foreach (var combatantId in records.Keys)
        {
            if (!plannedActionsJson.TryGetValue(combatantId, out var actionJson)) continue;
            logger.LogDebug("ResolveRound: combatant={CombatantId} action={ActionJson}", combatantId, actionJson);
            var array = JsonSerializer.Deserialize<PlannedActionArray>(actionJson, _jsonOptions);
            if (array is null) continue;

            var queue = new List<PlannedAction>();
            foreach (var entry in array.Actions)
                if (RulesWireMapping.ToPlannedAction(entry) is { } action)
                    queue.Add(action);
            plans[combatantId] = queue;
        }
        return plans;
    }

    // ----- Outbound mapping -----

    /// <summary>
    /// Every condition that can turn up this round, by id, in its stored shape (with its description): the
    /// ones combatants already carry, the ones their abilities apply, and Defend's built-in Guarded. The
    /// resolver's conditions carry no description, so results are mapped back through this.
    /// </summary>
    private static Dictionary<string, ConditionValues> ConditionLookup(Dictionary<string, CombatantRecord> records)
    {
        var lookup = new Dictionary<string, ConditionValues> { [RulesConfig.GuardedId] = ConditionCatalog.Guarded };
        foreach (var r in records.Values)
        {
            foreach (var effect in r.Entity.Profile?.Abilities?.SelectMany(a => a.Effects) ?? [])
                if (effect.Condition is { } c) lookup.TryAdd(c.Id, c);
            foreach (var stored in r.StoredConditions)
                lookup.TryAdd(stored.Condition.Id, stored.Condition);
        }
        return lookup;
    }

    /// <summary>The resolver's conditions in their stored shape, for Redis and the payload.</summary>
    private static List<ActiveConditionValues> ToStored(
        IReadOnlyList<ActiveCondition> conditions, Dictionary<string, ConditionValues> lookup) =>
        conditions.Select(c => new ActiveConditionValues
        {
            Condition = (lookup.TryGetValue(c.Id, out var v) ? v : ConditionMapping.FromRules(c.Definition)).Copy(),
            RoundsLeft = c.RoundsLeft,
            SourceId = c.SourceId,
        }).ToList();

    private static TickResult ToTickResult(
        TickOutcome tick, Dictionary<string, CombatantRecord> records, Dictionary<string, ConditionValues> conditionLookup)
    {
        string NameOf(string id) => records.TryGetValue(id, out var r) ? r.State.Name : "Unknown";
        string AbilityNameOf(string casterId, string abilityId) =>
            (records.TryGetValue(casterId, out var r) ? r.State.Profile.FindAbility(abilityId)?.Name : null) ?? abilityId;

        var movements = tick.Movements.Select(m => new MovementResult(
            m.CombatantId,
            NameOf(m.CombatantId),
            m.Path.Select(p => new PositionDto(p.X, p.Y)).ToList(),
            new PositionDto(m.FinalPosition.X, m.FinalPosition.Y),
            m.WasBlocked,
            m.BlockedById,
            m.AbilityId)).ToList();

        var attacks = tick.Attacks.Select(a =>
        {
            var attackerName = NameOf(a.AttackerId);
            var targetName = NameOf(a.TargetId);
            return new AttackResult(
                a.AttackerId, attackerName,
                a.TargetId, targetName,
                RulesWireMapping.ToWire(a.AttackType), a.Hit, a.Damage,
                a.TargetHpAfter, a.TargetDefeated, a.Wasted,
                ResolutionFeedFormatter.Attack(a, attackerName, targetName,
                    a.AbilityId is null ? null : AbilityNameOf(a.AttackerId, a.AbilityId)),
                a.Roll is { } r ? new AttackRollDto(r.D20, r.Accuracy, r.TargetDodge, r.ConditionAccuracy, r.ConditionDodge) : null,
                RulesWireMapping.ToWire(a.WastedReason),
                RulesWireMapping.ToWire(a.TargetStatus),
                a.FinishingBlow,
                a.AbilityId,
                a.MoveBonus,
                a.ConditionDamage);
        }).ToList();

        var effects = tick.Effects.Select(e => new EffectResult(
            e.CombatantId,
            RulesWireMapping.ToWire(e.Type),
            ResolutionFeedFormatter.Effect(e, NameOf(e.CombatantId), e.TargetId is null ? "" : NameOf(e.TargetId)),
            e.TargetId,
            RulesWireMapping.ToWire(e.WastedReason))).ToList();

        var abilityUses = tick.AbilityUses.Select(u =>
        {
            var name = NameOf(u.CombatantId);
            var abilityName = AbilityNameOf(u.CombatantId, u.AbilityId);
            return new AbilityUseResult(
                u.CombatantId, name, u.AbilityId, abilityName, u.TargetIds,
                RulesWireMapping.ToWire(u.WastedReason),
                ResolutionFeedFormatter.AbilityUse(u, name, abilityName, u.TargetIds.Select(NameOf).ToList()));
        }).ToList();

        var heals = tick.Heals.Select(h =>
        {
            var healerName = NameOf(h.HealerId);
            var targetName = NameOf(h.TargetId);
            return new HealResult(
                h.HealerId, healerName, h.TargetId, targetName, h.AbilityId, h.Amount, h.TargetHpAfter, h.Revived,
                RulesWireMapping.ToWire(h.TargetStatus), RulesWireMapping.ToWire(h.WastedReason),
                ResolutionFeedFormatter.Heal(h, healerName, targetName, AbilityNameOf(h.HealerId, h.AbilityId)));
        }).ToList();

        var conditions = tick.Conditions.Select(c =>
        {
            var sourceName = NameOf(c.SourceId);
            var targetName = NameOf(c.TargetId);
            var definition = conditionLookup.GetValueOrDefault(c.ConditionId);
            var conditionName = definition?.Name ?? c.ConditionId;
            var abilityName = c.AbilityId is null ? null : AbilityNameOf(c.SourceId, c.AbilityId);
            return new ConditionResult(
                c.SourceId, sourceName, c.TargetId, targetName, c.ConditionId, conditionName, definition?.Harmful ?? false,
                c.AbilityId, RulesWireMapping.ToWire(c.Result), c.RoundsLeft, RulesWireMapping.ToWire(c.WastedReason),
                ResolutionFeedFormatter.Condition(c, sourceName, targetName, conditionName, abilityName));
        }).ToList();

        return new TickResult(tick.Index, movements, attacks, effects, abilityUses, heals, conditions);
    }

    /// <summary>The resolved queues, in plan order, with each action's AP cost (so clients can show what ran in which tick).</summary>
    private static List<CombatantQueueDto> ToQueues(
        IReadOnlyDictionary<string, IReadOnlyList<PlannedAction>> plans, Dictionary<string, CombatantRecord> records)
    {
        var config = RulesConfig.Default;
        return plans
            .Where(p => records.ContainsKey(p.Key))
            .Select(p =>
            {
                var state = records[p.Key].State;
                return new CombatantQueueDto(
                    p.Key,
                    state.Name,
                    state.Profile.MaxAp,
                    p.Value.Select(a => new QueuedActionDto(
                        RulesWireMapping.ToWire(a.Type),
                        config.ApCost(a, state.Profile),
                        a.TargetCombatantId,
                        a.TargetCell is { } c ? new PositionDto(c.X, c.Y) : null,
                        a.AbilityId,
                        a.TargetCombatantIds)).ToList());
            })
            .ToList();
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
            foreach (var use in tick.AbilityUses ?? [])
            {
                events.Add(new GameEvent
                {
                    Id = Guid.NewGuid(),
                    EncounterId = encounterId,
                    OccurredAt = now,
                    EventType = use.WastedReason is null or "no_targets" ? "ability_used" : "ability_refused",
                    Payload = JsonSerializer.Serialize(new { use.CombatantId, use.AbilityId, use.TargetIds, use.WastedReason }, _jsonOptions),
                });
            }
            foreach (var heal in tick.Heals ?? [])
            {
                events.Add(new GameEvent
                {
                    Id = Guid.NewGuid(),
                    EncounterId = encounterId,
                    OccurredAt = now,
                    EventType = heal.WastedReason is not null ? "heal_wasted" : heal.Revived ? "heal_revived" : "heal",
                    Payload = JsonSerializer.Serialize(new { heal.HealerId, heal.TargetId, heal.AbilityId, heal.Amount, heal.TargetNewHp }, _jsonOptions),
                });
            }
            foreach (var condition in tick.Conditions ?? [])
            {
                events.Add(new GameEvent
                {
                    Id = Guid.NewGuid(),
                    EncounterId = encounterId,
                    OccurredAt = now,
                    EventType = condition.Result == "wasted" ? "condition_wasted" : $"condition_{condition.Result}",
                    Payload = JsonSerializer.Serialize(new { condition.SourceId, condition.TargetId, condition.ConditionId, condition.AbilityId, condition.RoundsLeft }, _jsonOptions),
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
