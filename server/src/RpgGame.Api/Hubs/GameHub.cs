using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace RpgGame.Api.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly IConnectionMultiplexer _redis;

    public GameHub(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task JoinSession(string sessionId, string displayName)
    {
        var groupName = $"session:{sessionId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        var db = _redis.GetDatabase();
        await db.HashSetAsync(
            $"session:{sessionId}:players",
            Context.UserIdentifier,
            displayName);

        await Clients.Group(groupName).SendAsync("PlayerPresenceChanged", new
        {
            userId = Context.UserIdentifier,
            displayName,
            isOnline = true,
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
