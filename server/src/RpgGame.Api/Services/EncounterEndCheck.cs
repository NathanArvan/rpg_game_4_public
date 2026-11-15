using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

/// <summary>
/// Decides whether an encounter is over after a round. Sides come from CombatantType
/// ("player" → party, everything else → enemy) until combatants get an explicit side (3B.0 PR 3).
/// </summary>
internal static class EncounterEndCheck
{
    public const string Party = "party";
    public const string Enemy = "enemy";
    public const string Draw = "draw";

    /// <returns>
    /// null if the encounter continues; otherwise the winning side, or <see cref="Draw"/> if both
    /// sides were wiped out. An encounter only ends if it has at least one combatant on each side,
    /// so a GM who started without monsters can still add one mid-encounter.
    /// </returns>
    public static string? Evaluate(IReadOnlyList<PlanningCombatantDto> combatants)
    {
        var party = combatants.Where(c => c.CombatantType == "player").ToList();
        var enemies = combatants.Where(c => c.CombatantType != "player").ToList();

        if (party.Count == 0 || enemies.Count == 0) return null;

        var partyStanding = party.Any(c => !c.IsDefeated);
        var enemiesStanding = enemies.Any(c => !c.IsDefeated);

        return (partyStanding, enemiesStanding) switch
        {
            (true, true) => null,
            (true, false) => Party,
            (false, true) => Enemy,
            (false, false) => Draw,
        };
    }
}
