using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RpgGame.Api.Contracts.Auth;
using RpgGame.Infrastructure.Identity;

namespace RpgGame.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly IConfiguration _config;

    public AuthController(UserManager<AppUser> users, IConfiguration config)
    {
        _users = users;
        _config = config;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), 200, "application/json")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req)
    {
        var user = new AppUser { UserName = req.Username, DisplayName = req.DisplayName };
        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return Ok(new AuthResponse(BuildToken(user)));
    }

    [HttpPost("token")]
    [ProducesResponseType(typeof(AuthResponse), 200, "application/json")]
    public async Task<ActionResult<AuthResponse>> Token([FromBody] TokenRequest req)
    {
        var user = await _users.FindByNameAsync(req.Username);
        if (user is null || !await _users.CheckPasswordAsync(user, req.Password))
            return Unauthorized();

        return Ok(new AuthResponse(BuildToken(user)));
    }

    private string BuildToken(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

