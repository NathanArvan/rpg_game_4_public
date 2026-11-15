namespace RpgGame.Api.Contracts.Hub;

public record QuickBattlePresetDto(string Id, string Name, string Description);

/// <summary>Returned to the caller of StartQuickBattle: the new session it now controls.</summary>
public record QuickBattleStartedResult(string JoinCode, Guid CampaignId, Guid GameSessionId, string PresetId);
