using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface IPlanningService
{
    Task<PlanActionResult> PlanActionAsync(string joinCode, string userId, PlannedActionArray action);
    Task<LockInResult> LockInAsync(string joinCode, string userId);
    Task<ForceResolveResult> ForceResolveAsync(string joinCode, string gmUserId);
}
