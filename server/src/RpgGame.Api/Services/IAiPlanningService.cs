namespace RpgGame.Api.Services;

public record AiLockIn(string CombatantId, string CharacterName);

public interface IAiPlanningService
{
    /// <summary>
    /// Plans this round's action for every living AI-controlled combatant that doesn't have a plan
    /// yet (so a GM override isn't overwritten), stores the plans and locks those combatants in.
    /// </summary>
    /// <param name="onlyCombatantId">Limit planning to one combatant (a monster added mid-round).</param>
    /// <returns>The combatants that were locked in, for the PlayerLockedIn broadcast.</returns>
    Task<IReadOnlyList<AiLockIn>> PlanAndLockInAsync(Guid encounterId, string? onlyCombatantId = null);
}
