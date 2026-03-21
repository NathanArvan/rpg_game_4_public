using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface IResolutionService
{
    Task<ResolutionResult> ResolveRoundAsync(string sessionId, string encounterId);
}
