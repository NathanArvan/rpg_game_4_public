namespace RpgGame.Domain.Entities;

public class EncounterCombatant
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    public string UserId { get; set; } = "";
    public Guid CharacterId { get; set; }
    public string CharacterName { get; set; } = "";
    public Encounter Encounter { get; set; } = null!;
}
