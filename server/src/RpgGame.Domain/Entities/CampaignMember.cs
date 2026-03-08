namespace RpgGame.Domain.Entities;

public class CampaignMember
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string UserId { get; set; } = "";  // FK → AspNetUsers.Id (string)
    public CampaignRole Role { get; set; }
    public Campaign Campaign { get; set; } = null!;
}
