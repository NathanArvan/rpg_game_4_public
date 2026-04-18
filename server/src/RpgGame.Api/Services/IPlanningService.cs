using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface IPlanningService
{
    Task<PlanActionResult> PlanActionAsync(string joinCode, string userId, PlannedActionArray action);
    Task<LockInResult> LockInAsync(string joinCode, string userId);
    Task<ForceResolveResult> ForceResolveAsync(string joinCode, string gmUserId);
    Task<PlanActionResult> PlanEnemyActionAsync(string joinCode, string gmUserId, string combatantId, PlannedActionArray action);
    Task<LockInResult> LockInEnemiesAsync(string joinCode, string gmUserId);
}
