using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public record QuickBattleStart(QuickBattleStartedResult Session, EncounterStartedResult Encounter);

public interface IQuickBattleService
{
    IReadOnlyList<QuickBattlePresetDto> ListPresets();

    /// <summary>
    /// Creates a campaign (caller is GM), a session and an encounter from the preset in one step.
    /// The caller controls every party combatant; enemies are AI-controlled. No planning timer.
    /// </summary>
    Task<QuickBattleStart> StartAsync(string userId, string presetId);
}
