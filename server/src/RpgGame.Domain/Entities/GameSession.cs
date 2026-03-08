namespace RpgGame.Domain.Entities;

public class GameSession
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string JoinCode { get; set; } = "";   // 6-char alphanumeric, unique
    public DateTimeOffset StartedAt { get; set; }
    public Campaign Campaign { get; set; } = null!;
}
