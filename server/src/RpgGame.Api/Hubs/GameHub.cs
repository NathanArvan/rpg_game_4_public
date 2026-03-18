using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using System.Text.Json;

namespace RpgGame.Api.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly IRedisGameState _redisState;
    private readonly AppDbContext _db;
    private readonly ILogger<GameHub> _logger;

    public GameHub(IRedisGameState redisState, AppDbContext db, ILogger<GameHub> logger)
    {
        _redisState = redisState;
        _db = db;
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}, User: {UserId}", Context.ConnectionId, Context.UserIdentifier);
        return base.OnConnectedAsync();
    }

    public async Task JoinSession(string joinCode, string displayName, string? characterId)
    {
        _logger.LogInformation("JoinSession called: joinCode={JoinCode}, displayName={DisplayName}, userId={UserId}", joinCode, displayName, Context.UserIdentifier);

        var gameSession = await _db.GameSessions
            .FirstOrDefaultAsync(s => s.JoinCode == joinCode);

        if (gameSession is null)
        {
            _logger.LogWarning("JoinSession: join code {JoinCode} not found", joinCode);
            throw new HubException("Invalid join code.");
        }

        var userId = Context.UserIdentifier!;

        // Validate characterId belongs to this user if provided
        if (!string.IsNullOrEmpty(characterId) && Guid.TryParse(characterId, out var charGuid))
        {
            var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == charGuid && c.UserId == userId);
            if (character is null)
                throw new HubException("Character not found or does not belong to you.");
        }
        else
        {
            characterId = null;
        }

        // Auto-join campaign_members if not already a member
        var existingMember = await _db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == userId);

        if (existingMember is null)
        {
            _db.CampaignMembers.Add(new CampaignMember
            {
                Id = Guid.NewGuid(),
                CampaignId = gameSession.CampaignId,
                UserId = userId,
                Role = CampaignRole.Player,
            });
            await _db.SaveChangesAsync();
        }

        var groupName = $"session:{gameSession.Id}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        // Send existing players to the joining client
        var existingPlayers = await _redisState.GetPlayersAsync(gameSession.Id.ToString());
        foreach (var (playerId, existing) in existingPlayers)
        {
            await Clients.Caller.SendAsync("PlayerPresenceChanged", new PlayerPresenceChangedPayload(
                playerId,
                existing.DisplayName,
                string.IsNullOrEmpty(existing.CharacterId) ? null : existing.CharacterId,
                true));
        }

        await _redisState.SetPlayerAsync(gameSession.Id.ToString(), userId, new PlayerEntry(displayName, characterId ?? ""));

        _logger.LogInformation("Broadcasting PlayerPresenceChanged to group {GroupName}", groupName);
        await Clients.Group(groupName).SendAsync("PlayerPresenceChanged", new PlayerPresenceChangedPayload(
            userId,
            displayName,
            characterId,
            true));
    }

    public async Task StartEncounter(string joinCode)
    {
        var userId = Context.UserIdentifier!;

        var gameSession = await _db.GameSessions
            .FirstOrDefaultAsync(s => s.JoinCode == joinCode);

        if (gameSession is null)
            throw new HubException("Invalid join code.");

        var member = await _db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == userId);

        if (member is null || member.Role != CampaignRole.Gm)
            throw new HubException("Only the GM can start an encounter.");

        var activeEncounter = await _db.Encounters
            .FirstOrDefaultAsync(e => e.GameSessionId == gameSession.Id && e.Status == EncounterStatus.Active);

        if (activeEncounter is not null)
            throw new HubException("An encounter is already active for this session.");

        var sessionId = gameSession.Id.ToString();
        var playerEntries = await _redisState.GetPlayersAsync(sessionId);

        if (playerEntries.Count == 0)
            throw new HubException("No players are connected.");

        var combatants = new List<EncounterCombatant>();
        var characterMap = new Dictionary<Guid, Character>();

        foreach (var (playerId, playerEntry) in playerEntries)
        {
            if (string.IsNullOrEmpty(playerEntry.CharacterId))
                continue;
            if (!Guid.TryParse(playerEntry.CharacterId, out var charId))
                continue;

            var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == charId);
            if (character is null) continue;

            characterMap[charId] = character;

            var dexMod = DexModifier(character.BaseStats.Dex);
            var roll = RollD20();

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
            throw new HubException("No players with characters are connected.");

        var encounter = new Encounter
        {
            Id = Guid.NewGuid(),
            GameSessionId = gameSession.Id,
            Status = EncounterStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            Combatants = combatants,
        };

        _db.Encounters.Add(encounter);
        await _db.SaveChangesAsync();

        // Order by initiative descending
        var ordered = combatants.OrderByDescending(c => c.Initiative).ToList();
        var activeTurnCombatantId = ordered[0].Id.ToString();

        // Auto-assign positions: combatant[i] → { x: i, y: 0 }
        const int gridWidth = 20;
        const int gridHeight = 20;
        var positions = new Dictionary<string, Position>();
        for (int i = 0; i < ordered.Count; i++)
            positions[ordered[i].Id.ToString()] = new Position(Math.Min(i, gridWidth - 1), 0);

        var mapEntry = new MapEntry(gridWidth, gridHeight, positions, new List<string>());
        var activeEncounterEntry = new ActiveEncounterEntry(
            encounter.Id.ToString(),
            activeTurnCombatantId,
            ordered.Select(c => new CombatantRef(c.Id.ToString(), c.UserId)).ToList()
        );

        await _redisState.SetActiveEncounterAsync(sessionId, activeEncounterEntry);
        await _redisState.SetMapAsync(encounter.Id.ToString(), mapEntry);

        // Seed character live state in Redis
        foreach (var c in ordered)
        {
            if (characterMap.TryGetValue(c.CharacterId, out var ch))
                await _redisState.SeedCharacterLiveStateAsync(c.CharacterId.ToString(), ch.CurrentHp, ch.Class);
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

        var groupName = $"session:{gameSession.Id}";
        await Clients.Group(groupName).SendAsync("EncounterStarted", payload);
    }

    public async Task DeclareAttack(string joinCode, string targetCombatantId)
    {
        var userId = Context.UserIdentifier!;

        var gameSession = await _db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode);
        if (gameSession is null)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Invalid join code."));
            return;
        }

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await _redisState.GetActiveEncounterAsync(sessionId);
        if (activeEncounter is null)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("No active encounter."));
            return;
        }

        // Verify it's caller's turn
        var activeCombatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == activeEncounter.ActiveTurnCombatantId);
        if (activeCombatantRef is null || activeCombatantRef.UserId != userId)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("It is not your turn."));
            return;
        }

        // No self-targeting
        if (activeCombatantRef.Id == targetCombatantId)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("You cannot attack yourself."));
            return;
        }

        // Target must exist
        var targetRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == targetCombatantId);
        if (targetRef is null)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Target not found."));
            return;
        }

        // Check defeated set
        if (await _redisState.IsDefeatedAsync(activeEncounter.EncounterId, activeCombatantRef.Id))
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("You are defeated."));
            return;
        }
        if (await _redisState.IsDefeatedAsync(activeEncounter.EncounterId, targetCombatantId))
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Target is already defeated."));
            return;
        }

        // Parse encounter Guid
        if (!Guid.TryParse(activeEncounter.EncounterId, out var encounterGuid))
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Invalid encounter ID."));
            return;
        }

        // Parse target combatant Guid
        if (!Guid.TryParse(targetCombatantId, out var targetCombatantGuid))
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Invalid target ID."));
            return;
        }

        // Get attacker's character for class (damage dice)
        var attackerDbCombatant = await _db.EncounterCombatants
            .FirstOrDefaultAsync(c => c.EncounterId == encounterGuid && c.UserId == userId);
        var attackerChar = attackerDbCombatant is not null
            ? await _db.Characters.FirstOrDefaultAsync(c => c.Id == attackerDbCombatant.CharacterId)
            : null;
        var attackerClass = attackerChar?.Class ?? "";
        var attackerName = attackerChar?.Name ?? "Unknown";

        // Get target's character for name and HP key
        var targetDbCombatant = await _db.EncounterCombatants
            .FirstOrDefaultAsync(c => c.Id == targetCombatantGuid);
        var targetChar = targetDbCombatant is not null
            ? await _db.Characters.FirstOrDefaultAsync(c => c.Id == targetDbCombatant.CharacterId)
            : null;
        var targetName = targetChar?.Name ?? "Unknown";

        if (targetDbCombatant is null)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Target combatant not found in DB."));
            return;
        }

        // Roll damage based on attacker's class
        var damage = RollDamage(attackerClass);
        var diceString = DamageDiceString(attackerClass);

        // Decrement target HP in Redis
        var newHpRaw = await _redisState.DecrementHpAsync(targetDbCombatant.CharacterId.ToString(), damage);
        var newHp = (int)Math.Max(0, newHpRaw);
        if (newHpRaw < 0)
            await _redisState.SetHpAsync(targetDbCombatant.CharacterId.ToString(), 0);

        var isDefeated = newHp <= 0;

        // Persist to game_events
        var eventPayload = JsonSerializer.Serialize(new
        {
            attackerId = activeCombatantRef.Id,
            attackerName,
            targetId = targetCombatantId,
            targetName,
            damageDice = diceString,
            damage,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        _db.GameEvents.Add(new GameEvent
        {
            Id = Guid.NewGuid(),
            EncounterId = encounterGuid,
            OccurredAt = DateTimeOffset.UtcNow,
            EventType = "attack",
            Payload = eventPayload,
        });
        await _db.SaveChangesAsync();

        if (isDefeated)
            await _redisState.MarkDefeatedAsync(activeEncounter.EncounterId, targetCombatantId);

        var feedText = isDefeated
            ? $"{attackerName} attacks {targetName} for {damage} damage! {targetName} is defeated!"
            : $"{attackerName} attacks {targetName} for {damage} damage! ({newHp} HP remaining)";

        var groupName = $"session:{gameSession.Id}";
        await Clients.Group(groupName).SendAsync("AttackResolved", new AttackResolvedPayload(
            activeCombatantRef.Id,
            attackerName,
            targetCombatantId,
            targetName,
            damage,
            newHp,
            feedText));

        if (isDefeated)
            await Clients.Group(groupName).SendAsync("CombatantDefeated", new CombatantDefeatedPayload(targetCombatantId));
    }

    public async Task MoveToken(string joinCode, string combatantId, int targetX, int targetY)
    {
        var userId = Context.UserIdentifier!;

        var gameSession = await _db.GameSessions
            .FirstOrDefaultAsync(s => s.JoinCode == joinCode);
        if (gameSession is null)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Invalid join code."));
            return;
        }

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await _redisState.GetActiveEncounterAsync(sessionId);
        if (activeEncounter is null)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("No active encounter."));
            return;
        }

        var map = await _redisState.GetMapAsync(activeEncounter.EncounterId);
        if (map is null)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Map data not found."));
            return;
        }

        // Verify caller owns this combatant
        var combatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == combatantId);
        if (combatantRef is null || combatantRef.UserId != userId)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("You do not own this combatant."));
            return;
        }

        // Verify it is their turn
        if (combatantId != activeEncounter.ActiveTurnCombatantId)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("It is not your turn."));
            return;
        }

        // Verify destination within grid bounds
        if (targetX < 0 || targetX >= map.GridWidth || targetY < 0 || targetY >= map.GridHeight)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Target position is out of bounds."));
            return;
        }

        // Verify Chebyshev distance ≤ 6
        if (!map.Positions.TryGetValue(combatantId, out var current))
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Current position not found."));
            return;
        }

        var chebyshev = Math.Max(Math.Abs(targetX - current.X), Math.Abs(targetY - current.Y));
        if (chebyshev > 6)
        {
            await Clients.Caller.SendAsync("ActionRejected", new ActionRejectedPayload("Move exceeds maximum range (6 squares)."));
            return;
        }

        // Update position
        var updatedPositions = new Dictionary<string, Position>(map.Positions) { [combatantId] = new Position(targetX, targetY) };
        var updatedMap = map with { Positions = updatedPositions };
        await _redisState.SetMapAsync(activeEncounter.EncounterId, updatedMap);

        var groupName = $"session:{gameSession.Id}";
        await Clients.Group(groupName).SendAsync("TokenMoved", new TokenMovedPayload(combatantId, targetX, targetY));
    }

    public async Task EndTurn(string joinCode)
    {
        var userId = Context.UserIdentifier!;

        var gameSession = await _db.GameSessions
            .FirstOrDefaultAsync(s => s.JoinCode == joinCode);
        if (gameSession is null)
            throw new HubException("Invalid join code.");

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await _redisState.GetActiveEncounterAsync(sessionId);
        if (activeEncounter is null)
            throw new HubException("No active encounter.");

        // Verify caller is the active combatant
        var activeCombatant = activeEncounter.Combatants.FirstOrDefault(c => c.Id == activeEncounter.ActiveTurnCombatantId);
        if (activeCombatant is null || activeCombatant.UserId != userId)
            throw new HubException("It is not your turn.");

        // Advance to next combatant (wrap around, skipping defeated)
        var currentIndex = activeEncounter.Combatants.FindIndex(c => c.Id == activeEncounter.ActiveTurnCombatantId);
        var nextIndex = currentIndex;

        bool foundAlive = false;
        for (int attempt = 0; attempt < activeEncounter.Combatants.Count; attempt++)
        {
            nextIndex = (nextIndex + 1) % activeEncounter.Combatants.Count;
            var isDefeated = await _redisState.IsDefeatedAsync(activeEncounter.EncounterId, activeEncounter.Combatants[nextIndex].Id);
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
                var encounter = await _db.Encounters.FindAsync(endedEncounterGuid);
                if (encounter is not null)
                {
                    encounter.Status = EncounterStatus.Ended;
                    await _db.SaveChangesAsync();
                }
            }

            await _redisState.DeleteActiveEncounterAsync(sessionId);

            var groupName2 = $"session:{gameSession.Id}";
            await Clients.Group(groupName2).SendAsync("EncounterEnded", new EncounterEndedPayload(Guid.Parse(activeEncounter.EncounterId)));
            return;
        }

        var nextCombatantId = activeEncounter.Combatants[nextIndex].Id;
        var updated = activeEncounter with { ActiveTurnCombatantId = nextCombatantId };
        await _redisState.SetActiveEncounterAsync(sessionId, updated);

        var groupName = $"session:{gameSession.Id}";
        await Clients.Group(groupName).SendAsync("TurnAdvanced", new TurnAdvancedPayload(nextCombatantId));
    }

    private static int DexModifier(int dex) => (int)Math.Floor((dex - 10) / 2.0);
    private static int RollD20() => Random.Shared.Next(1, 21);

    private static int RollDamage(string characterClass) => characterClass.ToLowerInvariant() switch
    {
        "fighter" or "paladin" or "ranger" => Random.Shared.Next(1, 9), // d8
        "rogue" or "bard" => Random.Shared.Next(1, 7),                  // d6
        _ => Random.Shared.Next(1, 5),                                   // d4
    };

    private static string DamageDiceString(string characterClass) => characterClass.ToLowerInvariant() switch
    {
        "fighter" or "paladin" or "ranger" => "1d8",
        "rogue" or "bard" => "1d6",
        _ => "1d4",
    };

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}, error: {Error}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
