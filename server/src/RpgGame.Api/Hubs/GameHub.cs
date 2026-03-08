using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using StackExchange.Redis;
using System.Text.Json;

namespace RpgGame.Api.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly IConnectionMultiplexer _redis;
    private readonly AppDbContext _db;
    private readonly ILogger<GameHub> _logger;

    private record PlayerEntry(string DisplayName, string CharacterId);

    public GameHub(IConnectionMultiplexer redis, AppDbContext db, ILogger<GameHub> logger)
    {
        _redis = redis;
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

        var redisDb = _redis.GetDatabase();
        var redisKey = $"session:{gameSession.Id}:players";

        // Send existing players to the joining client
        var existingPlayers = await redisDb.HashGetAllAsync(redisKey);
        foreach (var entry in existingPlayers)
        {
            var existing = JsonSerializer.Deserialize<PlayerEntry>(entry.Value.ToString()!);
            if (existing is null) continue;
            await Clients.Caller.SendAsync("PlayerPresenceChanged", new
            {
                userId = entry.Name.ToString(),
                displayName = existing.DisplayName,
                characterId = string.IsNullOrEmpty(existing.CharacterId) ? null : existing.CharacterId,
                isOnline = true,
            });
        }

        var playerEntry = new PlayerEntry(displayName, characterId ?? "");
        await redisDb.HashSetAsync(redisKey, userId, JsonSerializer.Serialize(playerEntry));

        _logger.LogInformation("Broadcasting PlayerPresenceChanged to group {GroupName}", groupName);
        await Clients.Group(groupName).SendAsync("PlayerPresenceChanged", new
        {
            userId,
            displayName,
            characterId,
            isOnline = true,
        });
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

        var redisDb = _redis.GetDatabase();
        var redisKey = $"session:{gameSession.Id}:players";
        var playerEntries = await redisDb.HashGetAllAsync(redisKey);

        if (playerEntries.Length == 0)
            throw new HubException("No players are connected.");

        var combatants = new List<EncounterCombatant>();
        foreach (var entry in playerEntries)
        {
            var playerEntry = JsonSerializer.Deserialize<PlayerEntry>(entry.Value.ToString()!);
            if (playerEntry is null || string.IsNullOrEmpty(playerEntry.CharacterId))
                continue;
            if (!Guid.TryParse(playerEntry.CharacterId, out var charGuid))
                continue;

            var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == charGuid);
            if (character is null) continue;

            var dexMod = DexModifier(character.BaseStats.Dex);
            var roll = RollD20();

            combatants.Add(new EncounterCombatant
            {
                Id = Guid.NewGuid(),
                UserId = entry.Name.ToString(),
                CharacterId = charGuid,
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

        var payload = new
        {
            encounterId = encounter.Id,
            combatants = combatants.OrderByDescending(c => c.Initiative).Select(c => new
            {
                userId = c.UserId,
                characterId = c.CharacterId,
                characterName = c.CharacterName,
                initiative = c.Initiative,
                initiativeRoll = c.InitiativeRoll,
                dexModifier = c.DexModifier,
            }),
        };

        var groupName = $"session:{gameSession.Id}";
        await redisDb.StringSetAsync($"session:{gameSession.Id}:active_encounter", JsonSerializer.Serialize(payload));
        await Clients.Group(groupName).SendAsync("EncounterStarted", payload);
    }

    private static int DexModifier(int dex) => (int)Math.Floor((dex - 10) / 2.0);
    private static int RollD20() => Random.Shared.Next(1, 21);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}, error: {Error}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
