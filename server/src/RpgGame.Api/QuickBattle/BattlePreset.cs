using RpgGame.Domain.Entities;

namespace RpgGame.Api.QuickBattle;

/// <summary>A ready-made battle loaded from <c>QuickBattle/Presets/*.json</c>.</summary>
public sealed record BattlePreset(
    string Id,
    string Name,
    string Description,
    int Order,
    int GridWidth,
    int GridHeight,
    IReadOnlyList<PresetCombatant> Combatants);

/// <param name="Side">"party" (controlled by the player) or "enemy".</param>
/// <param name="Profile">The combatant's combat profile with the preset's overrides applied.</param>
/// <param name="IsAiControlled">Enemies default to AI-controlled.</param>
/// <param name="StartHp">HP at the start of the battle; null = max HP (3B.3).</param>
public sealed record PresetCombatant(
    string Name,
    string Side,
    CombatProfileValues Profile,
    int X,
    int Y,
    bool IsAiControlled,
    int? StartHp = null);
