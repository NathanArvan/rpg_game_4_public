using RpgGame.Rules;
using RpgGame.Api.CombatProfiles;
using RpgGame.Api.Contracts.CombatProfiles;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Domain.Entities;

namespace RpgGame.Api.Services;

/// <summary>Builds the combatant wire DTOs, including the combat profile fields the client shows.</summary>
internal static class CombatantDtos
{
    public static CombatantDto Combatant(EncounterCombatant c, int currentHp) => new(
        c.Id.ToString(),
        c.UserId,
        c.CharacterId?.ToString() ?? "",
        c.CharacterName,
        currentHp,
        CombatProfileMapping.MaxHp(c.Profile),
        c.CombatantType,
        c.IsAiControlled,
        c.Profile?.ProfileId ?? "",
        c.Profile?.ProfileName ?? "",
        CombatProfileMapping.MovementRange(c.Profile),
        CombatProfileMapping.MaxAp(c.Profile),
        Abilities(c.Profile));

    /// <param name="cooldowns">Abilities on cooldown at the start of the planning phase (3B.3); null = none.</param>
    public static PlanningCombatantDto Planning(
        EncounterCombatant c, int currentHp, CombatantStatus status = CombatantStatus.Standing, int bleedoutRoundsLeft = 0,
        IReadOnlyDictionary<string, int>? cooldowns = null) => new(
        c.Id.ToString(),
        c.UserId,
        c.CharacterName,
        currentHp,
        CombatProfileMapping.MaxHp(c.Profile),
        status != CombatantStatus.Standing,
        c.CombatantType,
        c.IsAiControlled,
        c.Profile?.ProfileId ?? "",
        c.Profile?.ProfileName ?? "",
        CombatProfileMapping.MovementRange(c.Profile),
        CombatProfileMapping.MaxAp(c.Profile),
        RulesWireMapping.ToWire(status),
        bleedoutRoundsLeft,
        Abilities(c.Profile),
        cooldowns is null ? [] : new Dictionary<string, int>(cooldowns));

    public static CombatProfileDto Profile(CombatProfileDefinition p) => new(
        p.Id, p.Name, p.Description, p.Side, p.MaxHp, p.MovementRange, p.Speed,
        p.MeleeAccuracy, p.RangedAccuracy, p.Dodge, p.BaseDamage, p.MaxAp,
        p.Abilities.Select(a => new CombatProfileAbilityDto(a.Id, a.Name, a.Description, a.ApCost, a.Cooldown)).ToList());

    /// <summary>The loadout stored on the combatant (3B.3); rows from before 3B.3 have none.</summary>
    public static List<AbilityDto> Abilities(CombatProfileValues? profile) =>
        (profile?.Abilities ?? []).Select(Ability).ToList();

    public static AbilityDto Ability(AbilityValues a) => new(
        a.Id, a.Name, a.Description, a.ApCost, a.Cooldown, a.Tags,
        new AbilityTargetingDto(a.Target.Kind, a.Target.Filter, a.Target.Range, a.Target.Count, a.Target.IncludeSelf),
        a.Effects.FirstOrDefault(e => e.Kind == "move") is { } move
            ? new AbilityMoveDto(move.To ?? "cell", move.MaxCells, move.UsesMovementBudget)
            : null);
}
