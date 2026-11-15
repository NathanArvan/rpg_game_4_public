namespace RpgGame.Api.Contracts.Hub;

public record CombatantDto(
    string Id,
    string UserId,
    string CharacterId,
    string CharacterName,
    int CurrentHp,
    int MaxHp,
    string CombatantType = "player",
    bool IsAiControlled = false);
