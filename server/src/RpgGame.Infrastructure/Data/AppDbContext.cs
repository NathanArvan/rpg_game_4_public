using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Identity;

namespace RpgGame.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignMember> CampaignMembers => Set<CampaignMember>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<CampaignMember>(e =>
        {
            e.HasIndex(m => new { m.CampaignId, m.UserId }).IsUnique();
            e.HasOne(m => m.Campaign)
             .WithMany(c => c.Members)
             .HasForeignKey(m => m.CampaignId);
            e.HasOne<AppUser>()
             .WithMany()
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<GameSession>(e =>
        {
            e.HasIndex(s => s.JoinCode).IsUnique();
            e.HasOne(s => s.Campaign)
             .WithMany(c => c.Sessions)
             .HasForeignKey(s => s.CampaignId);
        });
    }
}
