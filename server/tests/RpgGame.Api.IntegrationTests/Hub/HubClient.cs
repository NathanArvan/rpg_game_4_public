using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using RpgGame.Api.Contracts.Auth;
using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.IntegrationTests.Hub;

/// <summary>A broadcast as it arrived on the wire.</summary>
public sealed record ReceivedEvent(string Name, JsonElement Payload);

/// <summary>
/// A registered user with a real SignalR connection to the in-memory API, over WebSockets with the
/// JWT in the query string, as the browser does. Records every server → client event (raw JSON,
/// so tests can check the wire shape) and lets tests wait for them.
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HubConnection _connection;
    private readonly List<ReceivedEvent> _events = [];
    private readonly HashSet<int> _taken = [];
    private readonly Lock _lock = new();

    private HubClient(HubConnection connection, string userName)
    {
        _connection = connection;
        UserName = userName;
        foreach (var name in AllEventNames)
            _connection.On<JsonElement>(name, payload => { lock (_lock) _events.Add(new ReceivedEvent(name, payload.Clone())); });
    }

    public string UserName { get; }

    /// <summary>Every event name in <see cref="HubEvents.Events"/>.</summary>
    public static IReadOnlyList<string> AllEventNames { get; } = typeof(HubEvents.Events)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    /// <summary>Everything received so far, in arrival order.</summary>
    public IReadOnlyList<ReceivedEvent> Events
    {
        get { lock (_lock) return [.. _events]; }
    }

    /// <summary>Registers a new user over REST and connects them to the hub.</summary>
    public static async Task<HubClient> ConnectNewUserAsync(ApiFactory api)
    {
        var userName = $"it_{Guid.NewGuid():N}"[..20];
        using var http = api.CreateClient();
        var response = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest(userName, userName, "secret123"));
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;

        var server = api.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/game"), o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                // The token goes in the query string, as the browser sends it (Program.cs's
                // OnMessageReceived reads it from there); negotiate uses the bearer header.
                o.WebSocketFactory = async (context, ct) =>
                    await server.CreateWebSocketClient().ConnectAsync(
                        new Uri($"{context.Uri}&access_token={Uri.EscapeDataString(token)}"), ct);
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        var client = new HubClient(connection, userName);
        await connection.StartAsync();
        return client;
    }

    public Task<T> InvokeAsync<T>(string method, params object?[] args) =>
        _connection.InvokeCoreAsync<T>(method, args);

    public Task InvokeAsync(string method, params object?[] args) =>
        _connection.InvokeCoreAsync(method, args);

    /// <summary>
    /// The oldest event called <paramref name="name"/> (matching <paramref name="match"/>) that no
    /// earlier call returned, waiting for it if it hasn't arrived yet.
    /// </summary>
    public async Task<T> NextAsync<T>(string name, Func<T, bool>? match = null, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (_lock)
            {
                for (var i = 0; i < _events.Count; i++)
                {
                    if (_taken.Contains(i) || _events[i].Name != name) continue;
                    var payload = _events[i].Payload.Deserialize<T>(Json)!;
                    if (match is not null && !match(payload)) continue;
                    _taken.Add(i);
                    return payload;
                }
            }
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"{UserName}: no {name} arrived. Received: {string.Join(", ", Events.Select(e => e.Name))}");
            await Task.Delay(20);
        }
    }

    /// <summary>All events called <paramref name="name"/> received so far.</summary>
    public IReadOnlyList<T> All<T>(string name) =>
        Events.Where(e => e.Name == name).Select(e => e.Payload.Deserialize<T>(Json)!).ToList();

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
