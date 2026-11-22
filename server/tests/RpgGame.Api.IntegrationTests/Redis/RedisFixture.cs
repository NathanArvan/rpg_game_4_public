using RpgGame.Api.Services;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace RpgGame.Api.IntegrationTests.Redis;

/// <summary>
/// One real Redis (a Testcontainers container) shared by every Redis test. Tests use fresh
/// session / encounter / combatant ids, so they never see each other's keys.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    /// <summary>The class under test, on the real connection.</summary>
    public RedisGameState State { get; private set; } = null!;

    /// <summary>Raw access, to check what was written where (key names and stored formats).</summary>
    public IDatabase Db => Connection.GetDatabase();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        State = new RedisGameState(Connection);
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
        await _container.DisposeAsync();
    }

    public static string NewId() => Guid.NewGuid().ToString();
}

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "redis";
}
