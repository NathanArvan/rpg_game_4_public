namespace RpgGame.Api.Contracts.Hub;

/// <param name="Abilities">The combatant's ability loadout (3B.3).</param>
/// <param name="Conditions">Conditions on the combatant (3B.4).</param>
public record CombatantDto(
    string Id,
    string UserId,
    string CharacterId,
    string CharacterName,
    int CurrentHp,
    int MaxHp,
    string CombatantType = "player",
    bool IsAiControlled = false,
    string ProfileId = "",
    string ProfileName = "",
    int MovementRange = 6,
    int MaxAp = 4,
    IReadOnlyList<AbilityDto>? Abilities = null,
    IReadOnlyList<ActiveConditionDto>? Conditions = null);
