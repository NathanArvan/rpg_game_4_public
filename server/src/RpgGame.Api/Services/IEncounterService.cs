using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface IEncounterService
{
    Task<EncounterStartedResult> StartEncounterAsync(string joinCode, string userId);
}
