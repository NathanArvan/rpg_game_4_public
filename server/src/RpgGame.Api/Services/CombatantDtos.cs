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
        CombatProfileMapping.MaxAp(c.Profile));

    public static PlanningCombatantDto Planning(EncounterCombatant c, int currentHp, bool isDefeated) => new(
        c.Id.ToString(),
        c.UserId,
        c.CharacterName,
        currentHp,
        CombatProfileMapping.MaxHp(c.Profile),
        isDefeated,
        c.CombatantType,
        c.IsAiControlled,
        c.Profile?.ProfileId ?? "",
        c.Profile?.ProfileName ?? "",
        CombatProfileMapping.MovementRange(c.Profile),
        CombatProfileMapping.MaxAp(c.Profile));

    public static CombatProfileDto Profile(CombatProfileDefinition p) => new(
        p.Id, p.Name, p.Description, p.Side, p.MaxHp, p.MovementRange, p.Speed,
        p.MeleeAccuracy, p.RangedAccuracy, p.Dodge, p.BaseDamage, p.MaxAp);
}
