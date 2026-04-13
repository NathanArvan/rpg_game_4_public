using Microsoft.AspNetCore.SignalR;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Hubs;
using System.Collections.Concurrent;
using System.Text.Json;

namespace RpgGame.Api.Services;

public class PlanningTimerService(
    IHubContext<GameHub> hubContext,
    IRedisGameState redisState,
    IServiceScopeFactory scopeFactory,
    ILogger<PlanningTimerService> logger)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _timers = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public void Start(Guid encounterId, Guid sessionId, int durationSeconds)
    {
        var cts = new CancellationTokenSource();
        _timers[encounterId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds), cts.Token);
                await OnTimerExpired(encounterId, sessionId);
            }
            catch (OperationCanceledException)
            {
                // Timer was cancelled because all players locked in early — no action needed.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Planning timer error for encounter {EncounterId}", encounterId);
            }
            finally
            {
                _timers.TryRemove(encounterId, out _);
            }
        });
    }

    public void Cancel(Guid encounterId)
    {
        if (_timers.TryRemove(encounterId, out var cts))
            cts.Cancel();
    }

    private async Task OnTimerExpired(Guid encounterId, Guid sessionId)
    {
        logger.LogInformation("Planning timer expired for encounter {EncounterId}", encounterId);

        var sessionIdStr = sessionId.ToString();
        var encounterIdStr = encounterId.ToString();

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr);
        if (activeEncounter is null || activeEncounter.RoundPhase != "planning") return;

        var defendAction = JsonSerializer.Serialize(
            new PlannedActionArray(new List<PlannedActionEntry> { new("defend", null, null) }),
            _jsonOptions);

        var missingCombatantIds = new List<string>();
        foreach (var combatant in activeEncounter.Combatants)
        {
            if (await redisState.IsDefeatedAsync(encounterIdStr, combatant.Id)) continue;
            if (!await redisState.HasPlannedActionAsync(encounterIdStr, combatant.Id))
                await redisState.SetPlannedActionAsync(encounterIdStr, combatant.Id, defendAction);
            if (!await redisState.IsLockedInAsync(encounterIdStr, combatant.Id))
                missingCombatantIds.Add(combatant.Id);
        }

        if (missingCombatantIds.Count > 0)
            await redisState.AddLockedInBatchAsync(encounterIdStr, missingCombatantIds);

        var groupName = $"session:{sessionId}";
        await hubContext.Clients.Group(groupName)
            .SendAsync(HubEvents.Events.AllLockedIn, new AllLockedInPayload());

        logger.LogInformation("Planning timer: AllLockedIn broadcast sent for encounter {EncounterId}", encounterId);

        await RunResolutionAsync(encounterId, sessionId, groupName);
    }

    private async Task RunResolutionAsync(Guid encounterId, Guid sessionId, string groupName)
    {
        const int planningTimerSeconds = 30;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var resolutionSvc = scope.ServiceProvider.GetRequiredService<IResolutionService>();

            var resolution = await resolutionSvc.ResolveRoundAsync(encounterId);
            await hubContext.Clients.Group(groupName)
                .SendAsync(HubEvents.Events.ResolutionCompleted, resolution);

            await Task.Delay(3000);

            var nextTimerAt = DateTimeOffset.UtcNow.AddSeconds(planningTimerSeconds);
            await redisState.StartNextPlanningPhaseAsync(sessionId.ToString(), resolution.NewRoundNumber, nextTimerAt);

            var planningPayload = new PlanningPhaseStartedPayload(
                resolution.NewRoundNumber,
                planningTimerSeconds,
                resolution.UpdatedCombatants);
            await hubContext.Clients.Group(groupName)
                .SendAsync(HubEvents.Events.PlanningPhaseStarted, planningPayload);

            Start(encounterId, sessionId, planningTimerSeconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Resolution error after timer expiry for encounter {EncounterId}", encounterId);
        }
    }
}
