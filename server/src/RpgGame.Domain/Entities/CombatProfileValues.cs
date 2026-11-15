namespace RpgGame.Domain.Entities;

/// <summary>
/// A combatant's resolved combat profile (roadmap 3B.1), copied from a preset when the combatant
/// joins an encounter, with any battle-preset overrides already applied. Stored as JSON on
/// <see cref="EncounterCombatant"/> so a running encounter never depends on the preset catalog.
/// </summary>
public class CombatProfileValues
{
    /// <summary>Preset key, e.g. "bruiser".</summary>
    public string ProfileId { get; set; } = "";
    /// <summary>Preset display name, e.g. "Bruiser".</summary>
    public string ProfileName { get; set; } = "";
    public int MaxHp { get; set; }
    public int MovementRange { get; set; }
    public int Speed { get; set; }
    public int MeleeAccuracy { get; set; }
    public int RangedAccuracy { get; set; }
    public int Dodge { get; set; }
    /// <summary>Damage die, e.g. "d8".</summary>
    public string BaseDamage { get; set; } = "";
    /// <summary>Action points per round (3B.2). Null on rows stored before 3B.2: read as the default.</summary>
    public int? MaxAp { get; set; }
}
