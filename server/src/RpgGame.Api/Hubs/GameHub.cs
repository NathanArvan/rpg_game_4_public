using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace RpgGame.Api.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<GameHub> _logger;

    public GameHub(IConnectionMultiplexer redis, ILogger<GameHub> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}, User: {UserId}", Context.ConnectionId, Context.UserIdentifier);
        return base.OnConnectedAsync();
    }

    public async Task JoinSession(string sessionId, string displayName)
    {
        _logger.LogInformation("JoinSession called: sessionId={SessionId}, displayName={DisplayName}, userId={UserId}", sessionId, displayName, Context.UserIdentifier);

        var groupName = $"session:{sessionId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        var db = _redis.GetDatabase();

        // Send existing players to the joining client before adding them
        var existing = await db.HashGetAllAsync($"session:{sessionId}:players");
        foreach (var entry in existing)
        {
            await Clients.Caller.SendAsync("PlayerPresenceChanged", new
            {
                userId = entry.Name.ToString(),
                displayName = entry.Value.ToString(),
                isOnline = true,
            });
        }

        await db.HashSetAsync(
            $"session:{sessionId}:players",
            Context.UserIdentifier,
            displayName);

        _logger.LogInformation("Broadcasting PlayerPresenceChanged to group {GroupName}", groupName);
        await Clients.Group(groupName).SendAsync("PlayerPresenceChanged", new
        {
            userId = Context.UserIdentifier,
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
