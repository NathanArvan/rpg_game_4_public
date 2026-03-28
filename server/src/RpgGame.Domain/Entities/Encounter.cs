namespace RpgGame.Domain.Entities;

public enum EncounterStatus { Active, Ended }

public class Encounter
{
    public Guid Id { get; set; }
    public Guid GameSessionId { get; set; }
    public EncounterStatus Status { get; set; } = EncounterStatus.Active;
    public DateTimeOffset StartedAt { get; set; }
    public GameSession GameSession { get; set; } = null!;
    public ICollection<EncounterCombatant> Combatants { get; set; } = [];
}
