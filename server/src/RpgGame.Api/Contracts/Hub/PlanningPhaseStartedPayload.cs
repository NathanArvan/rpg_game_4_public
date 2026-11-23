namespace RpgGame.Api.Contracts.Hub;

/// <param name="ApCosts">AP cost per action type, keyed by wire type ("move", "melee_attack", …) (3B.2).</param>
public record PlanningPhaseStartedPayload(
    int RoundNumber,
    int PlanningTimerDurationSeconds,
    IReadOnlyList<PlanningCombatantDto> Combatants,
    Dictionary<string, int> ApCosts);

/// <param name="IsDefeated">Out of the fight: downed, stable or dead.</param>
/// <param name="Status">"standing", "downed", "stable" or "dead" (3B.5).</param>
/// <param name="BleedoutRoundsLeft">While downed: rounds left before bleeding out.</param>
/// <param name="Abilities">The combatant's ability loadout (3B.3).</param>
/// <param name="Cooldowns">Abilities on cooldown at the start of this planning phase: id → rounds left (3B.3).</param>
/// <param name="Conditions">Conditions on the combatant at the start of this planning phase (3B.4).</param>
public record PlanningCombatantDto(
    string Id,
    string UserId,
    string CharacterName,
    int CurrentHp,
    int MaxHp,
    bool IsDefeated,
    string CombatantType = "player",
    bool IsAiControlled = false,
    string ProfileId = "",
    string ProfileName = "",
    int MovementRange = 6,
    int MaxAp = 4,
    string Status = "standing",
    int BleedoutRoundsLeft = 0,
    IReadOnlyList<AbilityDto>? Abilities = null,
    Dictionary<string, int>? Cooldowns = null,
    IReadOnlyList<ActiveConditionDto>? Conditions = null);
