using Microsoft.AspNetCore.SignalR;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Hubs;
using System.Collections.Concurrent;

namespace RpgGame.Api.Services;

public class PlanningTimerService : BackgroundService
{
    private readonly IHubContext<GameHub> _hubContext;
    private readonly IRedisGameState _redisState;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlanningTimerService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    public PlanningTimerService(
        IHubContext<GameHub> hubContext,
        IRedisGameState redisState,
        IServiceScopeFactory scopeFactory,
        ILogger<PlanningTimerService> logger)
    {
        _hubContext = hubContext;
        _redisState = redisState;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    public void ScheduleTimer(string encounterId, string sessionId, TimeSpan duration)
    {
        CancelTimer(encounterId);

        var cts = new CancellationTokenSource();
        _timers[encounterId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(duration, cts.Token);

                // Timer expired — check if still in planning phase
                var activeEncounter = await _redisState.GetActiveEncounterAsync(sessionId);
                if (activeEncounter is null || activeEncounter.RoundPhase != "planning")
                    return;

                // Transition to resolving
                var updated = activeEncounter with { RoundPhase = "resolving" };
                await _redisState.SetActiveEncounterAsync(sessionId, updated);

                _logger.LogInformation("Planning timer expired for encounter {EncounterId}, transitioning to resolving", encounterId);

                var groupName = $"session:{sessionId}";
                await _hubContext.Clients.Group(groupName)
                    .SendAsync(HubEvents.Events.AllLockedIn, new AllLockedInPayload(encounterId));

                // Resolve the round
                using var scope = _scopeFactory.CreateScope();
                var resolutionSvc = scope.ServiceProvider.GetRequiredService<IResolutionService>();
                var resolution = await resolutionSvc.ResolveRoundAsync(sessionId, encounterId);

                await _hubContext.Clients.Group(groupName)
                    .SendAsync(HubEvents.Events.ResolutionCompleted, resolution.Payload);

                if (resolution.NextRoundPayload is not null)
                {
                    await _hubContext.Clients.Group(groupName)
                        .SendAsync(HubEvents.Events.PlanningPhaseStarted, resolution.NextRoundPayload);

                    ScheduleTimer(
                        encounterId,
                        sessionId,
                        TimeSpan.FromMilliseconds(resolution.NextRoundPayload.PlanningTimerExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                }
                else
                {
                    await _hubContext.Clients.Group(groupName)
                        .SendAsync(HubEvents.Events.EncounterEnded, new EncounterEndedPayload(encounterId));
                }
            }
            catch (TaskCanceledException)
            {
                // Timer was cancelled (all locked in early)
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during timer-triggered resolution for encounter {EncounterId}", encounterId);
            }
            finally
            {
                _timers.TryRemove(encounterId, out _);
            }
        });
    }

    public void CancelTimer(string encounterId)
    {
        if (_timers.TryRemove(encounterId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
