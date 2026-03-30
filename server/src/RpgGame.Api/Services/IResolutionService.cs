using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface IResolutionService
{
    Task<ResolutionPayload> ResolveRoundAsync(Guid encounterId);
}
