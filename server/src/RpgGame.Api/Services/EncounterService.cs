using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Services;

public class EncounterService(AppDbContext db, IRedisGameState redisState) : IEncounterService
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

        var combatants = new List<EncounterCombatant>();
        var characterMap = new Dictionary<Guid, Character>();

        foreach (var (playerId, playerEntry) in playerEntries)
        {
            if (string.IsNullOrEmpty(playerEntry.CharacterId)) continue;
            if (!Guid.TryParse(playerEntry.CharacterId, out var charId)) continue;

            var character = await db.Characters.FirstOrDefaultAsync(c => c.Id == charId);
            if (character is null) continue;

            characterMap[charId] = character;

            var dexMod = CombatDice.DexModifier(character.BaseStats.Dex);
            var roll = CombatDice.RollD20();

            combatants.Add(new EncounterCombatant
            {
                Id = Guid.NewGuid(),
                UserId = playerId,
                CharacterId = charId,
                CharacterName = character.Name,
                InitiativeRoll = roll,
                DexModifier = dexMod,
                Initiative = roll + dexMod,
            });
        }

        if (combatants.Count == 0)
            throw new GameActionException("No players with characters are connected.");

        var encounter = new Encounter
        {
            Id = Guid.NewGuid(),
            GameSessionId = gameSession.Id,
            Status = EncounterStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            Combatants = combatants,
        };

        db.Encounters.Add(encounter);
        await db.SaveChangesAsync();

        var ordered = combatants.OrderByDescending(c => c.Initiative).ToList();
        var activeTurnCombatantId = ordered[0].Id.ToString();

        const int gridWidth = 20;
        const int gridHeight = 20;
        var positions = new Dictionary<string, Position>();
        for (int i = 0; i < ordered.Count; i++)
            positions[ordered[i].Id.ToString()] = new Position(Math.Min(i, gridWidth - 1), 0);

        var mapEntry = new MapEntry(gridWidth, gridHeight, positions, new List<string>());
        var activeEncounterEntry = new ActiveEncounterEntry(
            encounter.Id.ToString(),
            activeTurnCombatantId,
            ordered.Select(c => new CombatantRef(c.Id.ToString(), c.UserId)).ToList());

        await redisState.SetActiveEncounterAsync(sessionId, activeEncounterEntry);
        await redisState.SetMapAsync(encounter.Id.ToString(), mapEntry);

        foreach (var c in ordered)
        {
            if (characterMap.TryGetValue(c.CharacterId, out var ch))
                await redisState.SeedCharacterLiveStateAsync(c.CharacterId.ToString(), ch.CurrentHp, ch.Class);
        }

        var combatantDtos = ordered.Select(c =>
        {
            characterMap.TryGetValue(c.CharacterId, out var ch);
            return new CombatantDto(
                c.Id.ToString(),
                c.UserId,
                c.CharacterId.ToString(),
                c.CharacterName,
                c.Initiative,
                c.InitiativeRoll,
                c.DexModifier,
                ch?.CurrentHp ?? 0,
                ch?.MaxHp ?? 0);
        }).ToList();

        var mapDto = new MapDto(
            gridWidth,
            gridHeight,
            positions.ToDictionary(kv => kv.Key, kv => new PositionDto(kv.Value.X, kv.Value.Y)),
            new List<string>());

        var payload = new EncounterStartedPayload(
            encounter.Id.ToString(),
            activeTurnCombatantId,
            combatantDtos,
            mapDto);

        return new EncounterStartedResult(gameSession.Id, payload);
    }

    public async Task<TokenMovedResult> MoveTokenAsync(string joinCode, string userId, string combatantId, int targetX, int targetY)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        var map = await redisState.GetMapAsync(activeEncounter.EncounterId)
            ?? throw new GameActionException("Map data not found.", isActionRejected: true);

        var combatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == combatantId);
        if (combatantRef is null || combatantRef.UserId != userId)
            throw new GameActionException("You do not own this combatant.", isActionRejected: true);

        if (combatantId != activeEncounter.ActiveTurnCombatantId)
            throw new GameActionException("It is not your turn.", isActionRejected: true);

        if (targetX < 0 || targetX >= map.GridWidth || targetY < 0 || targetY >= map.GridHeight)
            throw new GameActionException("Target position is out of bounds.", isActionRejected: true);

        if (!map.Positions.TryGetValue(combatantId, out var current))
            throw new GameActionException("Current position not found.", isActionRejected: true);

        var chebyshev = Math.Max(Math.Abs(targetX - current.X), Math.Abs(targetY - current.Y));
        if (chebyshev > 6)
            throw new GameActionException("Move exceeds maximum range (6 squares).", isActionRejected: true);

        var updatedPositions = new Dictionary<string, Position>(map.Positions) { [combatantId] = new Position(targetX, targetY) };
        await redisState.SetMapAsync(activeEncounter.EncounterId, map with { Positions = updatedPositions });

        return new TokenMovedResult(gameSession.Id, new TokenMovedPayload(combatantId, targetX, targetY));
    }

    public async Task<EndTurnResult> EndTurnAsync(string joinCode, string userId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.");

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.");

        var activeCombatant = activeEncounter.Combatants.FirstOrDefault(c => c.Id == activeEncounter.ActiveTurnCombatantId);
        if (activeCombatant is null || activeCombatant.UserId != userId)
            throw new GameActionException("It is not your turn.");

        var currentIndex = activeEncounter.Combatants.FindIndex(c => c.Id == activeEncounter.ActiveTurnCombatantId);
        var nextIndex = currentIndex;
        bool foundAlive = false;

        for (int attempt = 0; attempt < activeEncounter.Combatants.Count; attempt++)
        {
            nextIndex = (nextIndex + 1) % activeEncounter.Combatants.Count;
            var isDefeated = await redisState.IsDefeatedAsync(activeEncounter.EncounterId, activeEncounter.Combatants[nextIndex].Id);
            if (!isDefeated)
            {
                foundAlive = true;
                break;
            }
        }

        if (!foundAlive)
        {
            if (Guid.TryParse(activeEncounter.EncounterId, out var endedEncounterGuid))
            {
                var encounter = await db.Encounters.FindAsync(endedEncounterGuid);
                if (encounter is not null)
                {
                    encounter.Status = EncounterStatus.Ended;
                    await db.SaveChangesAsync();
                }
            }

            await redisState.DeleteActiveEncounterAsync(sessionId);

            return new EncounterEndedResult(
                gameSession.Id,
                new EncounterEndedPayload(Guid.Parse(activeEncounter.EncounterId)));
        }

        var nextCombatantId = activeEncounter.Combatants[nextIndex].Id;
        var updated = activeEncounter with { ActiveTurnCombatantId = nextCombatantId };
        await redisState.SetActiveEncounterAsync(sessionId, updated);

        return new TurnAdvancedResult(gameSession.Id, new TurnAdvancedPayload(nextCombatantId));
    }
}
