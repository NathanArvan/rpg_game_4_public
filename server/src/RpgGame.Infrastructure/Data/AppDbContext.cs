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
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<EncounterCombatant> EncounterCombatants => Set<EncounterCombatant>();
    public DbSet<GameEvent> GameEvents => Set<GameEvent>();

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

        builder.Entity<Character>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            e.OwnsOne(c => c.BaseStats, b => b.ToJson());
            e.Property(c => c.CombatProfile).HasDefaultValue("skirmisher");
        });

        builder.Entity<Encounter>(e =>
        {
            e.Property(enc => enc.Status).HasConversion<string>();
            e.HasOne(enc => enc.GameSession).WithMany()
             .HasForeignKey(enc => enc.GameSessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(enc => enc.GameSessionId)
             .HasFilter("\"Status\" = 'Active'").IsUnique();
        });

        builder.Entity<EncounterCombatant>(e =>
        {
            e.HasOne(c => c.Encounter).WithMany(enc => enc.Combatants)
             .HasForeignKey(c => c.EncounterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Character>().WithMany().HasForeignKey(c => c.CharacterId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsOne(c => c.Profile, p => p.ToJson());
        });

        builder.Entity<GameEvent>(e =>
        {
            e.HasOne(ge => ge.Encounter).WithMany()
             .HasForeignKey(ge => ge.EncounterId).OnDelete(DeleteBehavior.Cascade);
            e.Property(ge => ge.Payload).HasColumnType("jsonb");
        });
    }
}
