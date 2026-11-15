using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;

namespace RpgGame.Api.Services;

/// <summary>A combatant (with its combat profile already set) and the HP it starts the encounter on.</summary>
internal sealed record CombatantStart(EncounterCombatant Combatant, int CurrentHp);

/// <summary>
/// The shared second half of starting an encounter, once its rows are saved: write the Redis live
/// state (active encounter, map, HP) and build the EncounterStarted / PlanningPhaseStarted payloads.
/// Used by the GM flow (EncounterService) and Quick Battle.
/// </summary>
internal static class EncounterLaunch
{
    public static async Task<EncounterStartedResult> PublishAsync(
        IRedisGameState redisState,
        Guid sessionId,
        Encounter encounter,
        IReadOnlyList<CombatantStart> starts,
        MapEntry map,
        bool planningTimerEnabled)
    {
        var sessionIdStr = sessionId.ToString();
        var timerSeconds = planningTimerEnabled ? GameConstants.PlanningTimerSeconds : 0;

        var activeEncounterEntry = new ActiveEncounterEntry(
            encounter.Id.ToString(),
            "planning",
            1,
            DateTimeOffset.UtcNow.AddSeconds(timerSeconds),
            starts.Select(s => new CombatantRef(
                s.Combatant.Id.ToString(), s.Combatant.UserId, s.Combatant.CombatantType, s.Combatant.IsAiControlled)).ToList(),
            planningTimerEnabled);

        await redisState.SetActiveEncounterAsync(sessionIdStr, activeEncounterEntry);
        await redisState.SetMapAsync(encounter.Id.ToString(), map);
        foreach (var s in starts)
            await redisState.SeedCharacterLiveStateAsync(
                EncounterStateLoader.HpKeyFor(s.Combatant), s.CurrentHp, s.Combatant.Profile?.ProfileId ?? "");

        var combatantDtos = starts.Select(s => CombatantDtos.Combatant(s.Combatant, s.CurrentHp)).ToList();

        var mapDto = new MapDto(
            map.GridWidth,
            map.GridHeight,
            map.Positions.ToDictionary(kv => kv.Key, kv => new PositionDto(kv.Value.X, kv.Value.Y)),
            new List<string>());

        var planningCombatantDtos = starts.Select(s => CombatantDtos.Planning(s.Combatant, s.CurrentHp, false)).ToList();

        return new EncounterStartedResult(
            sessionId,
            new EncounterStartedPayload(encounter.Id.ToString(), combatantDtos, mapDto),
            new PlanningPhaseStartedPayload(1, timerSeconds, planningCombatantDtos, RulesWireMapping.ApCosts()));
    }
}
