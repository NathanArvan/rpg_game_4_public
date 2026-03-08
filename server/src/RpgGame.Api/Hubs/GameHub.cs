using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using StackExchange.Redis;

namespace RpgGame.Api.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly IConnectionMultiplexer _redis;
    private readonly AppDbContext _db;
    private readonly ILogger<GameHub> _logger;

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

    public async Task JoinSession(string joinCode, string displayName)
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
            await Clients.Caller.SendAsync("PlayerPresenceChanged", new
            {
                userId = entry.Name.ToString(),
                displayName = entry.Value.ToString(),
                isOnline = true,
            });
        }

        await redisDb.HashSetAsync(redisKey, userId, displayName);

        _logger.LogInformation("Broadcasting PlayerPresenceChanged to group {GroupName}", groupName);
        await Clients.Group(groupName).SendAsync("PlayerPresenceChanged", new
        {
            userId,
            displayName,
            isOnline = true,
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}, error: {Error}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
