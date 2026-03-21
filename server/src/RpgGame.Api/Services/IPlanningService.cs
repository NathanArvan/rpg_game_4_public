using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;

namespace RpgGame.Api.Services;

public interface IPlanningService
{
    Task<PlanActionResult> PlanActionAsync(string joinCode, string userId, PlannedAction action);
    Task<LockInResult> LockInAsync(string joinCode, string userId);
    Task<ForceResolveResult> ForceResolveAsync(string joinCode, string userId);
    Task<PlanEnemyActionResult> PlanEnemyActionAsync(string joinCode, string callerUserId, string combatantId, PlannedAction action);
    Task<LockInEnemiesResult> LockInEnemiesAsync(string joinCode, string callerUserId);
}
