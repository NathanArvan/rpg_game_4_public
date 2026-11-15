using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Api.QuickBattle;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Services;

public class QuickBattleService(
    AppDbContext db,
    IRedisGameState redisState,
    BattlePresetCatalog presets,
    ILogger<QuickBattleService> logger) : IQuickBattleService
{
    public IReadOnlyList<QuickBattlePresetDto> ListPresets() =>
        presets.All.Select(p => new QuickBattlePresetDto(p.Id, p.Name, p.Description)).ToList();

    public async Task<QuickBattleStart> StartAsync(string userId, string presetId)
    {
        var preset = presets.Find(presetId)
            ?? throw new GameActionException($"Unknown battle preset '{presetId}'.", isActionRejected: true);

        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign { Id = Guid.NewGuid(), Name = $"Quick Battle — {preset.Name}", CreatedAt = now };
        var member = new CampaignMember { Id = Guid.NewGuid(), CampaignId = campaign.Id, UserId = userId, Role = CampaignRole.Gm };
        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            CampaignId = campaign.Id,
            JoinCode = await JoinCodes.NewUniqueAsync(db),
            StartedAt = now,
        };

        // The caller owns every row: party combatants are theirs to plan, enemies are run by the AI.
        var placed = preset.Combatants.Select(p => (Preset: p, Combatant: new EncounterCombatant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CharacterId = null,
            CharacterName = p.Name,
            CombatantType = p.Side == BattlePresetCatalog.Party ? "player" : "monster",
            IsAiControlled = p.IsAiControlled,
            MaxHp = p.Hp,
            Speed = p.Speed,
            DamageDie = p.Damage,
        })).ToList();

        var encounter = new Encounter
        {
            Id = Guid.NewGuid(),
            GameSessionId = session.Id,
            Status = EncounterStatus.Active,
            RoundNumber = 1,
            StartedAt = now,
            Combatants = placed.Select(x => x.Combatant).ToList(),
        };

        db.Campaigns.Add(campaign);
        db.CampaignMembers.Add(member);
        db.GameSessions.Add(session);
        db.Encounters.Add(encounter);
        await db.SaveChangesAsync();

        var map = new MapEntry(
            preset.GridWidth,
            preset.GridHeight,
            placed.ToDictionary(x => x.Combatant.Id.ToString(), x => new Position(x.Preset.X, x.Preset.Y)),
            new List<string>());

        var starts = placed.Select(x => new CombatantStart(x.Combatant, x.Preset.Hp, x.Preset.Hp, "preset")).ToList();
        var encounterResult = await EncounterLaunch.PublishAsync(
            redisState, session.Id, encounter, starts, map, planningTimerEnabled: false);

        logger.LogInformation("QuickBattle: user={UserId} started preset={PresetId} session={SessionId} encounter={EncounterId}",
            userId, preset.Id, session.Id, encounter.Id);

        return new QuickBattleStart(
            new QuickBattleStartedResult(session.JoinCode, campaign.Id, session.Id, preset.Id),
            encounterResult);
    }
}
