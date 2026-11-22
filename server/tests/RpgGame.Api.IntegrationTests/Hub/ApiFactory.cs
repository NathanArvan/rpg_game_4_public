using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RpgGame.Api.CombatProfiles;
using RpgGame.Api.Hubs;
using RpgGame.Api.QuickBattle;
using RpgGame.Api.Services;
using RpgGame.Rules;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace RpgGame.Api.IntegrationTests.Hub;

/// <summary>
/// The real API in memory (<see cref="WebApplicationFactory{TEntryPoint}"/>) on a real Postgres
/// (startup runs the EF migrations) and a real Redis (live state and the SignalR backplane), both
/// Testcontainers. What differs from production:
/// <list type="bullet">
/// <item>dice are <see cref="TestDice"/>, scripted by each test;</item>
/// <item>no playback delay between a resolution and the next planning phase;</item>
/// <item>battle presets come from this project's <c>Presets/</c> folder (<c>it-*</c>).</item>
/// </list>
/// Shared by every hub test; tests are isolated by using their own users and Quick Battles.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public TestDice Dice { get; } = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting (not ConfigureAppConfiguration) so Program.cs sees these while it registers services.
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Redis:ConnectionString", _redis.GetConnectionString());
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDiceRoller>();
            services.AddSingleton<IDiceRoller>(Dice);

            services.RemoveAll<IRoundOrchestrator>();
            services.AddScoped<IRoundOrchestrator>(sp => new RoundOrchestrator(
                sp.GetRequiredService<IResolutionService>(),
                sp.GetRequiredService<IEncounterService>(),
                sp.GetRequiredService<IAiPlanningService>(),
                sp.GetRequiredService<IRedisGameState>(),
                sp.GetRequiredService<IPlanningTimer>(),
                sp.GetRequiredService<IHubContext<GameHub>>(),
                sp.GetRequiredService<ILogger<RoundOrchestrator>>())
            {
                PlaybackDelay = TimeSpan.Zero,
            });

            services.RemoveAll<BattlePresetCatalog>();
            services.AddSingleton(sp => new BattlePresetCatalog(
                Path.Combine(AppContext.BaseDirectory, "Presets"),
                sp.GetRequiredService<CombatProfileCatalog>()));
        });
    }

    /// <summary>Runs <paramref name="action"/> with a fresh DI scope (for the DbContext and other scoped services).</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public IRedisGameState Redis => Services.GetRequiredService<IRedisGameState>();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
