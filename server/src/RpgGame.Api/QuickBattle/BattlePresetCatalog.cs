using RpgGame.Rules;
using System.Text.Json;

namespace RpgGame.Api.QuickBattle;

/// <summary>
/// Loads and validates every battle preset once. A broken preset file stops startup with a message
/// naming the file, so mistakes show up immediately rather than mid-battle.
/// </summary>
public sealed class BattlePresetCatalog
{
    public const string Party = "party";
    public const string Enemy = "enemy";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, BattlePreset> _byId;

    public BattlePresetCatalog(string directory)
    {
        var presets = Directory.EnumerateFiles(directory, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(Load)
            .ToList();
        if (presets.Count == 0)
            throw new InvalidDataException($"No battle presets found in {directory}.");

        _byId = new Dictionary<string, BattlePreset>(StringComparer.Ordinal);
        foreach (var preset in presets)
            if (!_byId.TryAdd(preset.Id, preset))
                throw new InvalidDataException($"Two battle presets use the id '{preset.Id}'.");

        All = presets.OrderBy(p => p.Order).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
    }

    public static BattlePresetCatalog FromAppDirectory() =>
        new(Path.Combine(AppContext.BaseDirectory, "QuickBattle", "Presets"));

    /// <summary>Presets in menu order.</summary>
    public IReadOnlyList<BattlePreset> All { get; }

    public BattlePreset? Find(string id) => _byId.GetValueOrDefault(id);

    // ----- Loading & validation -----

    internal static BattlePreset Load(string path)
    {
        var file = Path.GetFileName(path);
        PresetDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<PresetDto>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("empty file");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Battle preset {file}: invalid JSON ({ex.Message})", ex);
        }

        string Fail(string message) => throw new InvalidDataException($"Battle preset {file}: {message}");

        if (string.IsNullOrWhiteSpace(dto.Id)) Fail("'id' is required");
        if (string.IsNullOrWhiteSpace(dto.Name)) Fail("'name' is required");
        var grid = dto.Grid ?? [20, 20];
        if (grid.Length != 2 || grid[0] < 1 || grid[1] < 1) Fail("'grid' must be [width, height]");
        if (dto.Combatants is not { Count: > 0 }) Fail("'combatants' must list at least one combatant");

        var cells = new HashSet<(int, int)>();
        var combatants = new List<PresetCombatant>();
        foreach (var c in dto.Combatants!)
        {
            var who = string.IsNullOrWhiteSpace(c.Name) ? "a combatant" : c.Name;
            if (string.IsNullOrWhiteSpace(c.Name)) Fail("every combatant needs a 'name'");
            var side = c.Side?.Trim().ToLowerInvariant();
            if (side is not (Party or Enemy)) Fail($"{who}: 'side' must be \"party\" or \"enemy\"");
            if (c.Hp is not > 0) Fail($"{who}: 'hp' must be at least 1");
            try { DamageDie.Parse(c.Damage ?? ""); }
            catch (FormatException) { Fail($"{who}: 'damage' must be a die like \"d6\""); }
            if (c.At is not { Length: 2 }) Fail($"{who}: 'at' must be [x, y]");
            var (x, y) = (c.At![0], c.At[1]);
            if (x < 0 || y < 0 || x >= grid[0] || y >= grid[1]) Fail($"{who}: 'at' [{x}, {y}] is outside the grid");
            if (!cells.Add((x, y))) Fail($"{who}: cell [{x}, {y}] is already taken");
            if (side == Party && c.Ai == true) Fail($"{who}: party combatants can't be AI-controlled yet");

            combatants.Add(new PresetCombatant(
                c.Name!.Trim(), side!, c.Hp!.Value, c.Damage!.Trim(), c.Speed ?? 10, x, y,
                IsAiControlled: side == Enemy && (c.Ai ?? true)));
        }

        if (combatants.All(c => c.Side != Party)) Fail("needs at least one party combatant");
        if (combatants.All(c => c.Side != Enemy)) Fail("needs at least one enemy");

        return new BattlePreset(dto.Id!.Trim(), dto.Name!.Trim(), dto.Description?.Trim() ?? "",
            dto.Order ?? 100, grid[0], grid[1], combatants);
    }

    private sealed class PresetDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? Order { get; set; }
        public int[]? Grid { get; set; }
        public List<CombatantDto>? Combatants { get; set; }
    }

    private sealed class CombatantDto
    {
        public string? Name { get; set; }
        public string? Side { get; set; }
        public int? Hp { get; set; }
        public string? Damage { get; set; }
        public int? Speed { get; set; }
        public int[]? At { get; set; }
        public bool? Ai { get; set; }
    }
}
