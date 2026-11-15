using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using System.Text.Json;

namespace RpgGame.Api.Services;

public class EncounterService(AppDbContext db, IRedisGameState redisState, ILogger<EncounterService> logger) : IEncounterService
{

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

        // Inject any monsters staged before the encounter was started
        var pendingMonsters = await redisState.GetPendingMonstersAsync(sessionId);
        foreach (var pending in pendingMonsters)
        {
            combatants.Add(new EncounterCombatant
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CharacterId = null,
                CharacterName = pending.Name,
                CombatantType = "monster",
                IsAiControlled = pending.IsAiControlled,
            });
        }

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
        var playerCombatants = combatants.Where(c => c.CombatantType == "player").ToList();
        var monsterCombatants = combatants.Where(c => c.CombatantType == "monster").ToList();
        for (int i = 0; i < playerCombatants.Count; i++)
            positions[playerCombatants[i].Id.ToString()] = new Position(Math.Min(i, gridWidth - 1), 0);
        for (int i = 0; i < monsterCombatants.Count; i++)
            positions[monsterCombatants[i].Id.ToString()] = new Position(Math.Min(i, gridWidth - 1), gridHeight - 1);

        var mapEntry = new MapEntry(gridWidth, gridHeight, positions, new List<string>());

        // Monsters have no Character row: 20 HP and the fighter die (see EncounterStateLoader).
        var starts = combatants.Select(c =>
        {
            if (c.CombatantType == "monster")
                return new CombatantStart(c, 20, 20, "fighter");
            var character = characterMap[c.CharacterId!.Value];
            return new CombatantStart(c, character.CurrentHp, character.MaxHp, character.Class);
        }).ToList();

        var result = await EncounterLaunch.PublishAsync(
            redisState, gameSession.Id, encounter, starts, mapEntry, planningTimerEnabled: true);

        await redisState.ClearPendingMonstersAsync(sessionId);

        return result;
    }

    public async Task<AddMonsterResult> AddMonsterAsync(string joinCode, string gmUserId, string monsterName, bool isAiControlled)
    {
        logger.LogInformation("AddMonster: joinCode={JoinCode} gmUserId={GmUserId} monsterName={MonsterName}", joinCode, gmUserId, monsterName);

        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var member = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == gmUserId);

        if (member is null || member.Role != CampaignRole.Gm)
            throw new GameActionException("Only the GM can add monsters.", isActionRejected: true);

        logger.LogInformation("AddMonster: GM validated, sessionId={SessionId}", gameSession.Id);

        var trimmedName = string.IsNullOrWhiteSpace(monsterName) ? "Monster" : monsterName.Trim();
        var sessionId = gameSession.Id.ToString();
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId);

        if (activeEncounter is null)
        {
            await redisState.AddPendingMonsterAsync(sessionId, new PendingMonster(trimmedName, isAiControlled));
            var tempId = Guid.NewGuid().ToString();
            var tempDto = new PlanningCombatantDto(tempId, gmUserId, trimmedName, 20, 20, false, "monster", isAiControlled);
            logger.LogInformation("AddMonster: no active encounter — staged pending monster '{Name}'", trimmedName);
            return new AddMonsterResult(gameSession.Id, "", tempDto, new PositionDto(0, 0));
        }

        logger.LogInformation("AddMonster: activeEncounter found, encounterId={EncounterId} phase={Phase} combatantCount={Count}",
            activeEncounter.EncounterId, activeEncounter.RoundPhase, activeEncounter.Combatants.Count);

        if (activeEncounter.RoundPhase != "planning")
            throw new GameActionException("Monsters can only be added during the planning phase.", isActionRejected: true);

        var encounterGuid = GuidParser.TryParse(activeEncounter.EncounterId)
            ?? throw new GameActionException("Invalid encounter ID.", isActionRejected: true);

        var monster = new EncounterCombatant
        {
            Id = Guid.NewGuid(),
            EncounterId = encounterGuid,
            UserId = gmUserId,
            CharacterId = null,
            CharacterName = trimmedName,
            CombatantType = "monster",
            IsAiControlled = isAiControlled,
        };

        db.EncounterCombatants.Add(monster);
        await db.SaveChangesAsync();

        var monsterId = monster.Id.ToString();
        logger.LogInformation("AddMonster: monster saved to DB, monsterId={MonsterId}", monsterId);

        // Find spawn position: scan y=19 downward, x=0 rightward
        var map = await redisState.GetMapAsync(activeEncounter.EncounterId)
            ?? throw new GameActionException("Map data not found.", isActionRejected: true);

        var occupied = new HashSet<(int, int)>(map.Positions.Values.Select(p => (p.X, p.Y)));
        logger.LogInformation("AddMonster: map found, occupiedCells={Count}", occupied.Count);

        Position? spawnPos = null;
        for (int y = map.GridHeight - 1; y >= 0 && spawnPos is null; y--)
            for (int x = 0; x < map.GridWidth && spawnPos is null; x++)
                if (!occupied.Contains((x, y)))
                    spawnPos = new Position(x, y);

        spawnPos ??= new Position(0, 0); // fallback if grid is completely full
        logger.LogInformation("AddMonster: spawn position=({X},{Y})", spawnPos.X, spawnPos.Y);

        // Update Redis map
        map.Positions[monsterId] = spawnPos;
        await redisState.SetMapAsync(activeEncounter.EncounterId, map);

        // Seed monster HP using combatant ID as key (no Character row)
        await redisState.SeedCharacterLiveStateAsync(monsterId, 20, "fighter");

        // Add monster to active encounter combatant list
        activeEncounter.Combatants.Add(new CombatantRef(monsterId, gmUserId, "monster", isAiControlled));
        await redisState.SetActiveEncounterAsync(sessionId, activeEncounter);

        logger.LogInformation("AddMonster: Redis updated — map, HP seeded, encounter combatants updated");

        var monsterDto = new PlanningCombatantDto(
            monsterId, gmUserId, monster.CharacterName, 20, 20, false, "monster", isAiControlled);
        var spawnDto = new PositionDto(spawnPos.X, spawnPos.Y);

        logger.LogInformation("AddMonster: complete, returning MonsterAdded result");
        return new AddMonsterResult(gameSession.Id, activeEncounter.EncounterId, monsterDto, spawnDto);
    }

    public async Task EndEncounterAsync(Guid encounterId, string winningSide, int finalRoundNumber)
    {
        var encounter = await db.Encounters.FirstOrDefaultAsync(e => e.Id == encounterId)
            ?? throw new InvalidOperationException($"Encounter {encounterId} not found.");

        var now = DateTimeOffset.UtcNow;
        encounter.Status = EncounterStatus.Ended;
        encounter.EndedAt = now;
        encounter.RoundNumber = finalRoundNumber;

        db.GameEvents.Add(new GameEvent
        {
            Id = Guid.NewGuid(),
            EncounterId = encounterId,
            OccurredAt = now,
            EventType = "encounter_ended",
            Payload = JsonSerializer.Serialize(
                new { winningSide, roundNumber = finalRoundNumber },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
        });

        // DB before Redis, same as resolution persistence.
        await db.SaveChangesAsync();

        var encounterIdStr = encounterId.ToString();
        await redisState.ClearPlannedActionsAsync(encounterIdStr);
        await redisState.ClearLockedInAsync(encounterIdStr);
        await redisState.DeleteActiveEncounterAsync(encounter.GameSessionId.ToString());

        logger.LogInformation("EndEncounter: encounter={EncounterId} ended after round {Round}, winner={WinningSide}",
            encounterId, finalRoundNumber, winningSide);
    }
}
