using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Services;

public class SessionService(AppDbContext db, IRedisGameState redisState) : ISessionService
{
    public async Task<JoinSessionResult> JoinSessionAsync(string joinCode, string userId, string displayName, string? characterId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.");

        if (!string.IsNullOrEmpty(characterId) && Guid.TryParse(characterId, out var charGuid))
        {
            var character = await db.Characters.FirstOrDefaultAsync(c => c.Id == charGuid && c.UserId == userId)
                ?? throw new GameActionException("Character not found or does not belong to you.");
        }
        else
        {
            characterId = null;
        }

        var existingMember = await db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == gameSession.CampaignId && m.UserId == userId);

        if (existingMember is null)
        {
            db.CampaignMembers.Add(new CampaignMember
            {
                Id = Guid.NewGuid(),
                CampaignId = gameSession.CampaignId,
                UserId = userId,
                Role = CampaignRole.Player,
            });
            await db.SaveChangesAsync();
        }

        var sessionId = gameSession.Id.ToString();
        var existingPlayers = await redisState.GetPlayersAsync(sessionId);

        var existingPayloads = existingPlayers
            .Select(kvp => new PlayerPresenceChangedPayload(
                kvp.Key,
                kvp.Value.DisplayName,
                string.IsNullOrEmpty(kvp.Value.CharacterId) ? null : kvp.Value.CharacterId,
                true))
            .ToList();

        await redisState.SetPlayerAsync(sessionId, userId, new PlayerEntry(displayName, characterId ?? ""));

        var newPayload = new PlayerPresenceChangedPayload(userId, displayName, characterId, true);

        return new JoinSessionResult(gameSession.Id, existingPayloads, newPayload);
    }
}
