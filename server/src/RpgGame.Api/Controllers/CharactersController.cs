using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Characters;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Controllers;

[ApiController]
[Route("characters")]
[Authorize]
[Produces("application/json")]
public class CharactersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public CharactersController(AppDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CharacterResponse), 201, "application/json")]
    public async Task<ActionResult<CharacterResponse>> CreateCharacter([FromBody] CreateCharacterRequest request)
    {
        var userId = _user.UserId;
        if (userId is null) return Unauthorized();

        var stats = request.BaseStats;
        var maxHp = Services.CharacterStats.MaxHp(stats.Resilience);
        var character = new Character
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name,
            Race = request.Race ?? string.Empty,
            Class = request.Class ?? string.Empty,
            Level = request.Level,
            BaseStats = new BaseStats
            {
                Power     = stats.Power,
                Speed     = stats.Speed,
                Precision = stats.Precision,
                Resilience = stats.Resilience,
                Focus     = stats.Focus,
                Influence = stats.Influence,
            },
            MaxHp = maxHp,
            CurrentHp = maxHp,
            MovementRange = Services.CharacterStats.MovementRange(stats.Speed),
            VisionRange   = Services.CharacterStats.VisionRange(stats.Focus),
        };

        _db.Characters.Add(character);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCharacter), new { id = character.Id }, ToResponse(character));
    }

    [HttpGet("mine")]
    [ProducesResponseType(typeof(IEnumerable<CharacterResponse>), 200, "application/json")]
    public async Task<ActionResult<IEnumerable<CharacterResponse>>> GetMyCharacters()
    {
        var userId = _user.UserId;
        if (userId is null) return Unauthorized();

        var characters = await _db.Characters
            .Where(c => c.UserId == userId)
            .ToListAsync();

        return Ok(characters.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CharacterResponse), 200, "application/json")]
    public async Task<ActionResult<CharacterResponse>> GetCharacter(Guid id)
    {
        var userId = _user.UserId;
        if (userId is null) return Unauthorized();

        var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (character is null) return NotFound();

        return Ok(ToResponse(character));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CharacterResponse), 200, "application/json")]
    public async Task<ActionResult<CharacterResponse>> UpdateCharacter(Guid id, [FromBody] UpdateCharacterRequest request)
    {
        var userId = _user.UserId;
        if (userId is null) return Unauthorized();

        var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (character is null) return NotFound();

        var stats = request.BaseStats;
        var maxHp = Services.CharacterStats.MaxHp(stats.Resilience);
        character.Name  = request.Name;
        character.Race  = request.Race ?? string.Empty;
        character.Class = request.Class ?? string.Empty;
        character.Level = request.Level;
        character.BaseStats.Power     = stats.Power;
        character.BaseStats.Speed     = stats.Speed;
        character.BaseStats.Precision = stats.Precision;
        character.BaseStats.Resilience = stats.Resilience;
        character.BaseStats.Focus     = stats.Focus;
        character.BaseStats.Influence = stats.Influence;
        character.MaxHp        = maxHp;
        character.CurrentHp    = maxHp;
        character.MovementRange = Services.CharacterStats.MovementRange(stats.Speed);
        character.VisionRange   = Services.CharacterStats.VisionRange(stats.Focus);

        await _db.SaveChangesAsync();

        return Ok(ToResponse(character));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCharacter(Guid id)
    {
        var userId = _user.UserId;
        if (userId is null) return Unauthorized();

        var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (character is null) return NotFound();

        _db.Characters.Remove(character);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static CharacterResponse ToResponse(Character c) => new(
        c.Id,
        c.Name,
        c.Race,
        c.Class,
        c.Level,
        new BaseStatsDto(c.BaseStats.Power, c.BaseStats.Speed, c.BaseStats.Precision, c.BaseStats.Resilience, c.BaseStats.Focus, c.BaseStats.Influence),
        c.MaxHp,
        c.CurrentHp,
        c.MovementRange,
        c.VisionRange
    );
}

