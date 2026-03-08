namespace RpgGame.Domain.Entities;

public class Campaign
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<CampaignMember> Members { get; set; } = [];
    public ICollection<GameSession> Sessions { get; set; } = [];
}
