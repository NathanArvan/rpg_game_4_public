using RpgGame.Domain.Entities;
using RpgGame.Rules;
using System.Text.Json;

namespace RpgGame.Api.CombatProfiles;

/// <summary>A hand-authored combat profile preset from <c>CombatProfiles/profiles.json</c>.</summary>
/// <param name="Side">"party", "enemy" or "any"; only used to order pick lists.</param>
public sealed record CombatProfileDefinition(
    string Id,
    string Name,
    string Description,
    string Side,
    int MaxHp,
    int MovementRange,
    int Speed,
    int MeleeAccuracy,
    int RangedAccuracy,
    int Dodge,
    string BaseDamage,
    int MaxAp = CombatProfile.DefaultMaxAp)
{
    /// <summary>A fresh copy of these values to store on a combatant.</summary>
    public CombatProfileValues Snapshot() => new()
    {
        ProfileId = Id,
        ProfileName = Name,
        MaxHp = MaxHp,
        MovementRange = MovementRange,
        Speed = Speed,
        MeleeAccuracy = MeleeAccuracy,
        RangedAccuracy = RangedAccuracy,
        Dodge = Dodge,
        BaseDamage = BaseDamage,
        MaxAp = MaxAp,
    };
}

/// <summary>
/// Loads and validates the combat profile presets once. A broken file stops startup with a message
/// naming the profile, like <see cref="QuickBattle.BattlePresetCatalog"/>.
/// </summary>
public sealed class CombatProfileCatalog
{
    /// <summary>Profile for a character that hasn't picked one (or picked one that no longer exists).</summary>
    public const string DefaultPartyId = "skirmisher";
    /// <summary>Profile for a GM-added monster when none is chosen.</summary>
    public const string DefaultEnemyId = "grunt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Allowed range for a profile's (or preset override's) AP per round.</summary>
    public const int MinAp = 1, MaxApLimit = 10;

    private readonly Dictionary<string, CombatProfileDefinition> _byId;

    public CombatProfileCatalog(IReadOnlyList<CombatProfileDefinition> profiles)
    {
        _byId = new Dictionary<string, CombatProfileDefinition>(StringComparer.Ordinal);
        foreach (var p in profiles)
            if (!_byId.TryAdd(p.Id, p))
                throw new InvalidDataException($"Two combat profiles use the id '{p.Id}'.");
        foreach (var required in new[] { DefaultPartyId, DefaultEnemyId })
            if (!_byId.ContainsKey(required))
                throw new InvalidDataException($"Combat profiles: the default profile '{required}' is missing.");
        All = profiles;
    }

    public static CombatProfileCatalog FromFile(string path) => new(Load(path));

    public static CombatProfileCatalog FromAppDirectory() =>
        FromFile(Path.Combine(AppContext.BaseDirectory, "CombatProfiles", "profiles.json"));

    /// <summary>Profiles in file order.</summary>
    public IReadOnlyList<CombatProfileDefinition> All { get; }

    public CombatProfileDefinition? Find(string? id) => id is null ? null : _byId.GetValueOrDefault(id);

    public CombatProfileDefinition DefaultParty => _byId[DefaultPartyId];
    public CombatProfileDefinition DefaultEnemy => _byId[DefaultEnemyId];

    // ----- Loading & validation -----

    internal static List<CombatProfileDefinition> Load(string path)
    {
        var file = Path.GetFileName(path);
        FileDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("empty file");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Combat profiles {file}: invalid JSON ({ex.Message})", ex);
        }

        if (dto.Profiles is not { Count: > 0 })
            throw new InvalidDataException($"Combat profiles {file}: 'profiles' must list at least one profile");

        return dto.Profiles.Select(p => Validate(p, file)).ToList();
    }

    private static CombatProfileDefinition Validate(ProfileDto p, string file)
    {
        var who = string.IsNullOrWhiteSpace(p.Id) ? "a profile" : p.Id;
        string Fail(string message) => throw new InvalidDataException($"Combat profiles {file}: {who}: {message}");

        if (string.IsNullOrWhiteSpace(p.Id)) Fail("'id' is required");
        if (string.IsNullOrWhiteSpace(p.Name)) Fail("'name' is required");
        var side = p.Side?.Trim().ToLowerInvariant() ?? "any";
        if (side is not ("party" or "enemy" or "any")) Fail("'side' must be \"party\", \"enemy\" or \"any\"");
        if (p.Hp is not > 0) Fail("'hp' must be at least 1");
        if (p.Move is not > 0) Fail("'move' must be at least 1");
        if (p.Speed is null) Fail("'speed' is required");
        if (p.Dodge is null) Fail("'dodge' is required");
        try { DamageDie.Parse(p.Damage ?? ""); }
        catch (FormatException) { Fail("'damage' must be a die like \"d6\""); }
        if (p.Ap is < MinAp or > MaxApLimit) Fail($"'ap' must be between {MinAp} and {MaxApLimit}");

        return new CombatProfileDefinition(
            p.Id!.Trim(), p.Name!.Trim(), p.Description?.Trim() ?? "", side,
            p.Hp!.Value, p.Move!.Value, p.Speed!.Value,
            p.MeleeAccuracy ?? 0, p.RangedAccuracy ?? 0, p.Dodge!.Value, p.Damage!.Trim(),
            p.Ap ?? CombatProfile.DefaultMaxAp);
    }

    private sealed class FileDto
    {
        public List<ProfileDto>? Profiles { get; set; }
    }

    private sealed class ProfileDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Side { get; set; }
        public int? Hp { get; set; }
        public int? Move { get; set; }
        public int? Speed { get; set; }
        public int? MeleeAccuracy { get; set; }
        public int? RangedAccuracy { get; set; }
        public int? Dodge { get; set; }
        public string? Damage { get; set; }
        public int? Ap { get; set; }
    }
}

/// <summary>Maps stored profile values to the rules engine and to wire DTOs.</summary>
internal static class CombatProfileMapping
{
    /// <summary>Rows from before 3B.1 have no profile: they get the 3A placeholder values.</summary>
    public static CombatProfile ToRules(CombatProfileValues? v) => v is null
        ? CombatProfile.Default
        : new CombatProfile(v.MaxHp, v.MovementRange, v.Speed, v.MeleeAccuracy, v.RangedAccuracy, v.Dodge,
            DamageDie.Parse(v.BaseDamage), MaxAp(v));

    public static int MaxHp(CombatProfileValues? v) => v?.MaxHp ?? CombatProfile.Default.MaxHp;
    public static int MovementRange(CombatProfileValues? v) => v?.MovementRange ?? CombatProfile.Default.MovementRange;
    /// <summary>Rows from before 3B.2 have no MaxAp: they get the default.</summary>
    public static int MaxAp(CombatProfileValues? v) => v?.MaxAp ?? CombatProfile.DefaultMaxAp;
}
