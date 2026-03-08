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

        var character = new Character
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name,
            Race = request.Race,
            Class = request.Class,
            Level = request.Level,
            BaseStats = new BaseStats
            {
                Str = request.BaseStats.Str,
                Dex = request.BaseStats.Dex,
                Con = request.BaseStats.Con,
                Int = request.BaseStats.Int,
                Wis = request.BaseStats.Wis,
                Cha = request.BaseStats.Cha,
            },
            MaxHp = request.MaxHp,
            CurrentHp = request.CurrentHp,
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

        character.Name = request.Name;
        character.Race = request.Race;
        character.Class = request.Class;
        character.Level = request.Level;
        character.BaseStats.Str = request.BaseStats.Str;
        character.BaseStats.Dex = request.BaseStats.Dex;
        character.BaseStats.Con = request.BaseStats.Con;
        character.BaseStats.Int = request.BaseStats.Int;
        character.BaseStats.Wis = request.BaseStats.Wis;
        character.BaseStats.Cha = request.BaseStats.Cha;
        character.MaxHp = request.MaxHp;
        character.CurrentHp = request.CurrentHp;

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
        new BaseStatsDto(c.BaseStats.Str, c.BaseStats.Dex, c.BaseStats.Con, c.BaseStats.Int, c.BaseStats.Wis, c.BaseStats.Cha),
        c.MaxHp,
        c.CurrentHp
    );
}

