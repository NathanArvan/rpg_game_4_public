using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Services;

public class EncounterService(AppDbContext db, IRedisGameState redisState) : IEncounterService
{
    private const int PlanningTimerSeconds = 30;

    public async Task<EncounterStartedResult> StartEncounterAsync(string joinCode, string userId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.");

        var member = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == userId);

        if (member is null || member.Role != CampaignRole.Gm)
            throw new GameActionException("Only the GM can start an encounter.");

        var activeEncounter = await db.Encounters
            .FirstOrDefaultAsync(e => e.GameSessionId == gameSession.Id && e.Status == EncounterStatus.Active);

        if (activeEncounter is not null)
            throw new GameActionException("An encounter is already active for this session.");

        var sessionId = gameSession.Id.ToString();
        var playerEntries = await redisState.GetPlayersAsync(sessionId);

        if (playerEntries.Count == 0)
            throw new GameActionException("No players are connected.");

        var charIds = playerEntries.Values
            .Where(p => !string.IsNullOrEmpty(p.CharacterId))
            .Select(p => GuidParser.TryParse(p.CharacterId))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        var characterMap = await db.Characters
            .Where(c => charIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        var combatants = new List<EncounterCombatant>();

        foreach (var (playerId, playerEntry) in playerEntries)
        {
            if (string.IsNullOrEmpty(playerEntry.CharacterId)) continue;
            var charId = GuidParser.TryParse(playerEntry.CharacterId);
            if (charId is null) continue;
            if (!characterMap.TryGetValue(charId.Value, out var character)) continue;

            combatants.Add(new EncounterCombatant
            {
                Id = Guid.NewGuid(),
                UserId = playerId,
                CharacterId = charId.Value,
                CharacterName = character.Name,
                CombatantType = "player",
            });
        }

        if (combatants.Count == 0)
            throw new GameActionException("No players with characters are connected.");

        var encounter = new Encounter
        {
            Id = Guid.NewGuid(),
            GameSessionId = gameSession.Id,
            Status = EncounterStatus.Active,
            RoundNumber = 1,
            StartedAt = DateTimeOffset.UtcNow,
            Combatants = combatants,
        };

        db.Encounters.Add(encounter);
        await db.SaveChangesAsync();

        const int gridWidth = 20;
        const int gridHeight = 20;
        var positions = new Dictionary<string, Position>();
        for (int i = 0; i < combatants.Count; i++)
            positions[combatants[i].Id.ToString()] = new Position(Math.Min(i, gridWidth - 1), 0);

        var mapEntry = new MapEntry(gridWidth, gridHeight, positions, new List<string>());
        var planningTimerExpiresAt = DateTimeOffset.UtcNow.AddSeconds(PlanningTimerSeconds);
        var activeEncounterEntry = new ActiveEncounterEntry(
            encounter.Id.ToString(),
            "planning",
            1,
            planningTimerExpiresAt,
            combatants.Select(c => new CombatantRef(c.Id.ToString(), c.UserId, "player")).ToList());

        await redisState.SetActiveEncounterAsync(sessionId, activeEncounterEntry);
        await redisState.SetMapAsync(encounter.Id.ToString(), mapEntry);

        foreach (var c in combatants)
        {
            if (c.CharacterId.HasValue && characterMap.TryGetValue(c.CharacterId.Value, out var ch))
                await redisState.SeedCharacterLiveStateAsync(c.CharacterId.Value.ToString(), ch.CurrentHp, ch.Class);
        }

        var combatantDtos = combatants.Select(c =>
        {
            var hasChar = c.CharacterId.HasValue && characterMap.TryGetValue(c.CharacterId.Value, out var ch);
            var character = hasChar ? characterMap.GetValueOrDefault(c.CharacterId!.Value) : null;
            return new CombatantDto(
                c.Id.ToString(),
                c.UserId,
                c.CharacterId?.ToString() ?? "",
                c.CharacterName,
                character?.CurrentHp ?? 0,
                character?.MaxHp ?? 0,
                c.CombatantType);
        }).ToList();

        var mapDto = new MapDto(
            gridWidth,
            gridHeight,
            positions.ToDictionary(kv => kv.Key, kv => new PositionDto(kv.Value.X, kv.Value.Y)),
            new List<string>());

        var planningCombatantDtos = combatantDtos.Select(c =>
            new PlanningCombatantDto(c.Id, c.UserId, c.CharacterName, c.CurrentHp, c.MaxHp, false, c.CombatantType)).ToList();

        var encounterPayload = new EncounterStartedPayload(encounter.Id.ToString(), combatantDtos, mapDto);
        var planningPayload = new PlanningPhaseStartedPayload(1, PlanningTimerSeconds, planningCombatantDtos);

        return new EncounterStartedResult(gameSession.Id, encounterPayload, planningPayload);
    }

    public async Task<AddMonsterResult> AddMonsterAsync(string joinCode, string gmUserId, string monsterName)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var member = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == gmUserId);

        if (member is null || member.Role != CampaignRole.Gm)
            throw new GameActionException("Only the GM can add monsters.", isActionRejected: true);

        var sessionId = gameSession.Id.ToString();
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Monsters can only be added during the planning phase.", isActionRejected: true);

        var encounterGuid = GuidParser.TryParse(activeEncounter.EncounterId)
            ?? throw new GameActionException("Invalid encounter ID.", isActionRejected: true);

        var monster = new EncounterCombatant
        {
            Id = Guid.NewGuid(),
            EncounterId = encounterGuid,
            UserId = gmUserId,
            CharacterId = Guid.Empty,
            CharacterName = string.IsNullOrWhiteSpace(monsterName) ? "Monster" : monsterName.Trim(),
            CombatantType = "monster",
        };

        db.EncounterCombatants.Add(monster);
        await db.SaveChangesAsync();

        // Find spawn position: scan y=19 downward, x=0 rightward
        var map = await redisState.GetMapAsync(activeEncounter.EncounterId)
            ?? throw new GameActionException("Map data not found.", isActionRejected: true);

        var occupied = new HashSet<(int, int)>(map.Positions.Values.Select(p => (p.X, p.Y)));
        Position? spawnPos = null;
        for (int y = map.GridHeight - 1; y >= 0 && spawnPos is null; y--)
            for (int x = 0; x < map.GridWidth && spawnPos is null; x++)
                if (!occupied.Contains((x, y)))
                    spawnPos = new Position(x, y);

        spawnPos ??= new Position(0, 0); // fallback if grid is completely full

        var monsterId = monster.Id.ToString();

        // Update Redis map
        map.Positions[monsterId] = spawnPos;
        await redisState.SetMapAsync(activeEncounter.EncounterId, map);

        // Seed monster HP using combatant ID as key (no Character row)
        await redisState.SeedCharacterLiveStateAsync(monsterId, 20, "fighter");

        // Add monster to active encounter combatant list
        activeEncounter.Combatants.Add(new CombatantRef(monsterId, gmUserId, "monster"));
        await redisState.SetActiveEncounterAsync(sessionId, activeEncounter);

        var monsterDto = new PlanningCombatantDto(
            monsterId, gmUserId, monster.CharacterName, 20, 20, false, "monster");
        var spawnDto = new PositionDto(spawnPos.X, spawnPos.Y);

        return new AddMonsterResult(gameSession.Id, activeEncounter.EncounterId, monsterDto, spawnDto);
    }
}
