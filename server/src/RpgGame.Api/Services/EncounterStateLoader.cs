using Microsoft.EntityFrameworkCore;
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
    int MaxHp,
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
/// Builds the rules engine's view of an encounter from Postgres (combatants, characters) and Redis
/// (map, live HP, defeated set). Shared by resolution and AI planning so both see the same state.
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

        // Character stats (class, dex, maxHp) — monsters have null CharacterId, skip them
        var charIds = encounter.Combatants
            .Where(c => c.CharacterId.HasValue)
            .Select(c => c.CharacterId!.Value)
            .ToList();
        var characterMap = await db.Characters
            .Where(c => charIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        var mapEntry = await redisState.GetMapAsync(encounterIdStr)
            ?? throw new InvalidOperationException($"Map for encounter {encounterId} not found.");

        var records = new Dictionary<string, CombatantRecord>();
        foreach (var c in encounter.Combatants)
        {
            var combatantId = c.Id.ToString();
            var hpKey = HpKeyFor(c);
            var currentHp = await redisState.GetCharacterHpAsync(hpKey);
            var isDefeated = await redisState.IsDefeatedAsync(encounterIdStr, combatantId);
            characterMap.TryGetValue(c.CharacterId ?? Guid.Empty, out var character);
            records[combatantId] = ToRecord(c, hpKey, currentHp, isDefeated, character, mapEntry);
        }

        return new LoadedEncounter(encounter, mapEntry, records);
    }

    /// <summary>Redis live-state key: the character ID when there is one, otherwise the combatant ID.</summary>
    public static string HpKeyFor(EncounterCombatant c) => c.CharacterId?.ToString() ?? c.Id.ToString();

    /// <summary>
    /// Stat overrides on the combatant (Quick Battle presets) win over everything else. Otherwise,
    /// data-seeding defaults (not rules): monsters have no Character row, so they get MaxHp 20 and
    /// the fighter damage die; a player whose Character is missing gets MaxHp = current HP and the
    /// wizard die. Dex (default 10) feeds Speed until 3B.1 adds combat profiles.
    /// </summary>
    private static CombatantRecord ToRecord(
        EncounterCombatant c, string hpKey, int currentHp, bool isDefeated, Character? character, MapEntry map)
    {
        var isMonster = c.CombatantType == "monster";
        var maxHp = c.MaxHp ?? character?.MaxHp ?? (isMonster ? 20 : currentHp);
        var charClass = character?.Class ?? (isMonster ? "fighter" : "wizard");
        var speed = c.Speed ?? character?.BaseStats.Dex ?? 10;
        var damage = c.DamageDie is { } die ? Rules.DamageDie.Parse(die) : CombatMath.DamageDieForClass(charClass);
        GridPosition? position = map.Positions.TryGetValue(c.Id.ToString(), out var pos)
            ? new GridPosition(pos.X, pos.Y)
            : null; // the engine treats a missing position as (0, 0)

        var state = new CombatantState(
            Id: c.Id.ToString(),
            Name: c.CharacterName,
            Hp: currentHp,
            MaxHp: maxHp,
            Speed: speed,
            Damage: damage,
            Position: position,
            IsDefeated: isDefeated);

        return new CombatantRecord(hpKey, currentHp, maxHp, c, state);
    }
}
