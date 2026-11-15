using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RpgGame.Api.CombatProfiles;
using RpgGame.Api.Contracts.CombatProfiles;
using RpgGame.Api.Services;

namespace RpgGame.Api.Controllers;

/// <summary>Combat profile presets (3B.1), for the character page's profile picker.</summary>
[ApiController]
[Route("api/combat-profiles")]
[Authorize]
[Produces("application/json")]
public class CombatProfilesController(CombatProfileCatalog profiles) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CombatProfileDto>), 200, "application/json")]
    public ActionResult<IEnumerable<CombatProfileDto>> GetCombatProfiles() =>
        Ok(profiles.All.Select(CombatantDtos.Profile));
}
