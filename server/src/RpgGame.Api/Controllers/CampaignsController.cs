using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Controllers;

[ApiController]
[Route("campaigns")]
[Authorize]
public class CampaignsController : ControllerBase
{
    private readonly AppDbContext _db;

    public CampaignsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> CreateCampaign([FromBody] CreateCampaignRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (userId is null) return Unauthorized();

        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var membership = new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = campaign.Id,
            UserId = userId,
            Role = CampaignRole.Gm,
        };

        _db.Campaigns.Add(campaign);
        _db.CampaignMembers.Add(membership);
        await _db.SaveChangesAsync();

        return Ok(new { id = campaign.Id });
    }

    [HttpPost("{campaignId:guid}/sessions")]
    public async Task<IActionResult> StartSession(Guid campaignId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (userId is null) return Unauthorized();

        var member = await _db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == campaignId && m.UserId == userId);

        if (member is null || member.Role != CampaignRole.Gm)
            return Forbid();

        string joinCode;
        do
        {
            joinCode = GenerateJoinCode();
        } while (await _db.GameSessions.AnyAsync(s => s.JoinCode == joinCode));

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _db.GameSessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new { joinCode = session.JoinCode, gameSessionId = session.Id });
    }

    private static string GenerateJoinCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return string.Create(6, chars, (span, alphabet) =>
        {
            var bytes = RandomNumberGenerator.GetBytes(6);
            for (int i = 0; i < span.Length; i++)
                span[i] = alphabet[bytes[i] % alphabet.Length];
        });
    }
}

public record CreateCampaignRequest(string Name);
