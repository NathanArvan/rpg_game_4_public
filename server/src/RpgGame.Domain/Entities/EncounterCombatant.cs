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

    // Per-combatant stats for combatants without a Character row (Quick Battle presets). Null means
    // "use the Character / default". The precursor of 3B.1 combat profiles.
    public int? MaxHp { get; set; }
    public int? Speed { get; set; }
    /// <summary>Damage die, e.g. "d6".</summary>
    public string? DamageDie { get; set; }
    public Encounter Encounter { get; set; } = null!;
}
