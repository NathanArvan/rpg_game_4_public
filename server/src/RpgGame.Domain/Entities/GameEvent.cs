namespace RpgGame.Domain.Entities;

public class GameEvent
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public Encounter Encounter { get; set; } = null!;
}
