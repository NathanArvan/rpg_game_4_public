using RpgGame.Api.Contracts.Hub;
using RpgGame.Infrastructure.Data;
using RpgGame.Rules;
using System.Text.Json;

namespace RpgGame.Api.Services;

/// <summary>Adapter between persistence and <see cref="EnemyAi"/>: load state, plan, store, lock in.</summary>
public class AiPlanningService(
    AppDbContext db,
    IRedisGameState redisState,
    ILogger<AiPlanningService> logger) : IAiPlanningService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<AiLockIn>> PlanAndLockInAsync(Guid encounterId, string? onlyCombatantId = null)
    {
        var loaded = await EncounterStateLoader.LoadAsync(db, redisState, encounterId);
        var encounterIdStr = encounterId.ToString();

        var aiIds = new List<string>();
        foreach (var (id, record) in loaded.Records)
        {
            if (!record.Entity.IsAiControlled || record.State.IsDefeated) continue;
            if (onlyCombatantId is not null && id != onlyCombatantId) continue;
            if (await redisState.HasPlannedActionAsync(encounterIdStr, id)) continue; // GM override wins
            aiIds.Add(id);
        }
        if (aiIds.Count == 0) return [];

        // Sides come from CombatantType until combatants get an explicit side (3B.0 PR 3).
        var teamById = loaded.Records.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Entity.CombatantType == "player" ? EncounterEndCheck.Party : EncounterEndCheck.Enemy);

        var plans = EnemyAi.PlanRound(loaded.ToRulesState(), teamById, aiIds);

        var lockedIn = new List<AiLockIn>();
        foreach (var (id, queue) in plans)
        {
            var entries = queue.Select(RulesWireMapping.ToEntry).ToList();
            await redisState.SetPlannedActionAsync(encounterIdStr, id,
                JsonSerializer.Serialize(new PlannedActionArray(entries), _jsonOptions));
            lockedIn.Add(new AiLockIn(id, loaded.Records[id].State.Name));

            logger.LogInformation("AiPlanning: encounter={EncounterId} combatant={CombatantId} planned {Actions}",
                encounterId, id, string.Join(",", entries.Select(e => e.Type)));
        }

        await redisState.AddLockedInBatchAsync(encounterIdStr, lockedIn.Select(l => l.CombatantId));
        return lockedIn;
    }

    public async Task<AutoLockInResult> LockInUnableToActAsync(Guid encounterId)
    {
        var loaded = await EncounterStateLoader.LoadAsync(db, redisState, encounterId);
        var encounterIdStr = encounterId.ToString();
        var emptyPlan = JsonSerializer.Serialize(new PlannedActionArray([]), _jsonOptions);

        var lockedIn = new List<AiLockIn>();
        foreach (var (id, record) in loaded.Records)
        {
            var state = record.State;
            if (state.IsDefeated || !ConditionMath.CannotAct(state.Profile, state.Conditions)) continue;
            if (await redisState.IsLockedInAsync(encounterIdStr, id)) continue;
            await redisState.SetPlannedActionAsync(encounterIdStr, id, emptyPlan);
            lockedIn.Add(new AiLockIn(id, state.Name));
            logger.LogInformation("AutoLockIn: encounter={EncounterId} combatant={CombatantId} can't act this round", encounterId, id);
        }
        if (lockedIn.Count == 0) return new AutoLockInResult([], false);
        await redisState.AddLockedInBatchAsync(encounterIdStr, lockedIn.Select(l => l.CombatantId));

        bool all = true;
        foreach (var (id, record) in loaded.Records)
            if (!record.State.IsDefeated && !await redisState.IsLockedInAsync(encounterIdStr, id)) { all = false; break; }
        return new AutoLockInResult(lockedIn, all);
    }
}
