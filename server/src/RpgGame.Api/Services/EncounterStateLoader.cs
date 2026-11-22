using Microsoft.EntityFrameworkCore;
using RpgGame.Api.CombatProfiles;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using RpgGame.Rules;

namespace RpgGame.Api.Services;

/// <summary>Persistence-side facts about a combatant that the rules engine doesn't need.</summary>
/// <param name="HpKey">Redis live-state key: combatant ID for monsters, character ID for players.</param>
internal sealed record CombatantRecord(
    string HpKey,
    int StartingHp,
    EncounterCombatant Entity,
    CombatantState State);

internal sealed record LoadedEncounter(
    Encounter Encounter,
    MapEntry Map,
    Dictionary<string, CombatantRecord> Records)
{
    public EncounterState ToRulesState() =>
        new(Map.GridWidth, Map.GridHeight, Records.Values.Select(r => r.State).ToList());
}

/// <summary>
/// Builds the rules engine's view of an encounter from Postgres (combatants and their combat
/// profiles) and Redis (map, live HP, defeated set and statuses). Shared by resolution and AI planning so both see the same state.
/// </summary>
internal static class EncounterStateLoader
{
    public static async Task<LoadedEncounter> LoadAsync(AppDbContext db, IRedisGameState redisState, Guid encounterId)
    {
        var encounter = await db.Encounters
            .Include(e => e.Combatants)
            .FirstOrDefaultAsync(e => e.Id == encounterId)
            ?? throw new InvalidOperationException($"Encounter {encounterId} not found.");

        var encounterIdStr = encounterId.ToString();

        var mapEntry = await redisState.GetMapAsync(encounterIdStr)
            ?? throw new InvalidOperationException($"Map for encounter {encounterId} not found.");

        var records = new Dictionary<string, CombatantRecord>();
        foreach (var c in encounter.Combatants)
        {
            var combatantId = c.Id.ToString();
            var hpKey = HpKeyFor(c);
            var currentHp = await redisState.GetCharacterHpAsync(hpKey);
            var (status, bleedout) = await LoadStatusAsync(redisState, encounterIdStr, combatantId);
            var cooldowns = await redisState.GetCooldownsAsync(encounterIdStr, combatantId) ?? [];
            records[combatantId] = ToRecord(c, hpKey, currentHp, status, bleedout, cooldowns, mapEntry);
        }

        return new LoadedEncounter(encounter, mapEntry, records);
    }

    /// <summary>
    /// Standing unless in the defeated set; then the stored status (3B.5). A defeated combatant
    /// with no stored status (from before 3B.5) is dead.
    /// </summary>
    public static async Task<(CombatantStatus Status, int BleedoutRoundsLeft)> LoadStatusAsync(
        IRedisGameState redisState, string encounterId, string combatantId)
    {
        if (!await redisState.IsDefeatedAsync(encounterId, combatantId)) return (CombatantStatus.Standing, 0);
        var entry = await redisState.GetStatusAsync(encounterId, combatantId);
        return entry is not null && RulesWireMapping.ParseStatus(entry.Status) is { } status and not CombatantStatus.Standing
            ? (status, status == CombatantStatus.Downed ? entry.BleedoutRoundsLeft : 0)
            : (CombatantStatus.Dead, 0);
    }

    /// <summary>
    /// The combatant's team in the rules engine (3B.3): "party" for players, "enemy" for monsters,
    /// the same split <see cref="EncounterEndCheck"/> and AI planning use.
    /// </summary>
    public static string TeamOf(EncounterCombatant c) => c.CombatantType == "player" ? "party" : "enemy";

    /// <summary>Redis live-state key: the character ID when there is one, otherwise the combatant ID.</summary>
    public static string HpKeyFor(EncounterCombatant c) => c.CharacterId?.ToString() ?? c.Id.ToString();

    /// <summary>
    /// Every combat value comes from the profile stored on the combatant row (3B.1). Rows from
    /// before 3B.1 have none and get the default profile.
    /// </summary>
    private static CombatantRecord ToRecord(
        EncounterCombatant c, string hpKey, int currentHp, CombatantStatus status, int bleedout,
        IReadOnlyDictionary<string, int> cooldowns, MapEntry map)
    {
        GridPosition? position = map.Positions.TryGetValue(c.Id.ToString(), out var pos)
            ? new GridPosition(pos.X, pos.Y)
            : null; // not on the map

        var state = new CombatantState(
            Id: c.Id.ToString(),
            Name: c.CharacterName,
            Hp: currentHp,
            Profile: CombatProfileMapping.ToRules(c.Profile, c.CombatantType),
            Position: position,
            Status: status,
            BleedoutRoundsLeft: bleedout)
        {
            Team = TeamOf(c),
            Cooldowns = cooldowns,
        };

        return new CombatantRecord(hpKey, currentHp, c, state);
    }
}
