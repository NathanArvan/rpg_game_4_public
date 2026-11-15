namespace RpgGame.Api.Services;

/// <summary>Per-encounter planning countdown. Implemented by <see cref="PlanningTimerService"/>.</summary>
public interface IPlanningTimer
{
    void Start(Guid encounterId, Guid sessionId, int durationSeconds);
    void Cancel(Guid encounterId);
}
