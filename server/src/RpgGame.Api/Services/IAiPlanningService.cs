namespace RpgGame.Api.Services;

public record AiLockIn(string CombatantId, string CharacterName);

/// <param name="LockedIn">The combatants locked in, for the PlayerLockedIn broadcast.</param>
/// <param name="AllLockedIn">Every standing combatant is now locked in, so the round can resolve.</param>
public record AutoLockInResult(IReadOnlyList<AiLockIn> LockedIn, bool AllLockedIn);

public interface IAiPlanningService
{
    /// <summary>
    /// Plans this round's action for every living AI-controlled combatant that doesn't have a plan
    /// yet (so a GM override isn't overwritten), stores the plans and locks those combatants in.
    /// </summary>
    /// <param name="onlyCombatantId">Limit planning to one combatant (a monster added mid-round).</param>
    /// <returns>The combatants that were locked in, for the PlayerLockedIn broadcast.</returns>
    Task<IReadOnlyList<AiLockIn>> PlanAndLockInAsync(Guid encounterId, string? onlyCombatantId = null);

    /// <summary>
    /// Locks in, with an empty plan, every standing combatant whose conditions stop it acting this round
    /// (Stunned, or no AP: SPRINT_3B4 C8), player or monster, AI or not. Call it after <see cref="PlanAndLockInAsync"/>
    /// when a planning phase opens.
    /// </summary>
    Task<AutoLockInResult> LockInUnableToActAsync(Guid encounterId);
}
