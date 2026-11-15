namespace RpgGame.Domain.Entities;

public class EncounterCombatant
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    public string UserId { get; set; } = "";
    public Guid? CharacterId { get; set; }
    public string CharacterName { get; set; } = "";
    public string CombatantType { get; set; } = "player"; // "player" | "monster"
    /// <summary>Planned and locked in by the server each round (EnemyAi) instead of by a person.</summary>
    public bool IsAiControlled { get; set; }

    /// <summary>
    /// Combat values for this encounter, copied from the combatant's preset when it was created.
    /// Null only on rows from before 3B.1 (read as the default profile).
    /// </summary>
    public CombatProfileValues? Profile { get; set; }
    public Encounter Encounter { get; set; } = null!;
}
