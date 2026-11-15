namespace RpgGame.Api.Services;

public interface IRoundOrchestrator
{
    /// <summary>
    /// Runs everything that happens once every living combatant is locked in: resolves the round,
    /// broadcasts the results, then either ends the encounter or starts the next planning phase.
    /// Called from the hub (lock-in / force resolve) and from the planning timer.
    /// </summary>
    Task ResolveAndAdvanceAsync(Guid encounterId, Guid sessionId);
}
