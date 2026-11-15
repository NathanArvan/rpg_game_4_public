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
/// <param name="IsAiControlled">Enemies default to AI-controlled.</param>
public sealed record PresetCombatant(
    string Name,
    string Side,
    int Hp,
    string Damage,
    int Speed,
    int X,
    int Y,
    bool IsAiControlled);
