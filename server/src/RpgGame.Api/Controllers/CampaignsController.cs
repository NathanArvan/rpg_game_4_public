using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Campaigns;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Controllers;

[ApiController]
[Route("api/campaigns")]
[Authorize]
[Produces("application/json")]
public class CampaignsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public CampaignsController(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateCampaignResponse), 200, "application/json")]
    public async Task<ActionResult<CreateCampaignResponse>> CreateCampaign([FromBody] CreateCampaignRequest request)
    {
        var userId = _user.UserId;
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

        return Ok(new CreateCampaignResponse(campaign.Id));
    }

    [HttpPost("{campaignId:guid}/sessions")]
    [ProducesResponseType(typeof(StartSessionResponse), 200, "application/json")]
    public async Task<ActionResult<StartSessionResponse>> StartSession(Guid campaignId)
    {
        var userId = _user.UserId;
        if (userId is null) return Unauthorized();

        var member = await _db.CampaignMembers
            .FirstOrDefaultAsync(m => m.CampaignId == campaignId && m.UserId == userId);

        if (member is null || member.Role != CampaignRole.Gm)
            return Forbid();

        var joinCode = await JoinCodes.NewUniqueAsync(_db);

        var session = new GameSession
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _db.GameSessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new StartSessionResponse(session.JoinCode, session.Id));
    }
}
