namespace RpgGame.Api.Contracts.Hub;

public record CombatantDto(
    string Id,
    string UserId,
    string CharacterId,
    string CharacterName,
    int Initiative,
    int InitiativeRoll,
    int DexModifier,
    int CurrentHp,
    int MaxHp);
