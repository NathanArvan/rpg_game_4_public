using RpgGame.Api.Abilities;
using RpgGame.Domain.Entities;
using RpgGame.Rules;
using System.Text.Json;

namespace RpgGame.Api.CombatProfiles;

/// <summary>A hand-authored combat profile preset from <c>CombatProfiles/profiles.json</c>.</summary>
/// <param name="Side">"party", "enemy" or "any"; only used to order pick lists.</param>
/// <param name="AbilityIds">The loadout, as ability ids from <c>Abilities/abilities.json</c> (3B.3).</param>
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
    int MaxAp = CombatProfile.DefaultMaxAp,
    IReadOnlyList<string>? AbilityIds = null)
{
    /// <summary>The loadout's full definitions, filled in by <see cref="CombatProfileCatalog"/> from <see cref="AbilityIds"/>.</summary>
    public IReadOnlyList<AbilityValues> Abilities { get; init; } = [];

    /// <summary>Percent chance (0–100) to resist a harmful condition from an enemy (3B.4 PR 2).</summary>
    public int ConditionResist { get; init; }

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
        Abilities = Abilities.Select(a => a.Copy()).ToList(),
        ConditionResist = ConditionResist,
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

    /// <summary>Allowed range for <c>conditionResist</c> (a percentage).</summary>
    public const int MaxConditionResist = 100;

    private readonly Dictionary<string, CombatProfileDefinition> _byId;

    /// <param name="abilities">Where the profiles' ability ids are looked up (3B.3). Null: no abilities exist.</param>
    public CombatProfileCatalog(IReadOnlyList<CombatProfileDefinition> profiles, AbilityCatalog? abilities = null)
    {
        Abilities = abilities ?? AbilityCatalog.Empty;
        // Resolve each loadout once; every snapshot copies these definitions.
        profiles = profiles.Select(p => p with
        {
            Abilities = Abilities.Resolve(p.AbilityIds ?? [],
                message => new InvalidDataException($"Combat profiles: {p.Id}: {message}")),
        }).ToList();

        _byId = new Dictionary<string, CombatProfileDefinition>(StringComparer.Ordinal);
        foreach (var p in profiles)
            if (!_byId.TryAdd(p.Id, p))
                throw new InvalidDataException($"Two combat profiles use the id '{p.Id}'.");
        foreach (var required in new[] { DefaultPartyId, DefaultEnemyId })
            if (!_byId.ContainsKey(required))
                throw new InvalidDataException($"Combat profiles: the default profile '{required}' is missing.");
        All = profiles;
    }

    public static CombatProfileCatalog FromFile(string path, AbilityCatalog? abilities = null) => new(Load(path), abilities);

    /// <summary>The shipped profiles, with their abilities from <c>Abilities/abilities.json</c>.</summary>
    public static CombatProfileCatalog FromAppDirectory() =>
        FromFile(Path.Combine(AppContext.BaseDirectory, "CombatProfiles", "profiles.json"), AbilityCatalog.FromAppDirectory());

    /// <summary>The ability catalog loadouts come from (battle presets use it for their overrides).</summary>
    public AbilityCatalog Abilities { get; }

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
        if (p.ConditionResist is < 0 or > MaxConditionResist) Fail($"'conditionResist' must be between 0 and {MaxConditionResist}");

        return new CombatProfileDefinition(
            p.Id!.Trim(), p.Name!.Trim(), p.Description?.Trim() ?? "", side,
            p.Hp!.Value, p.Move!.Value, p.Speed!.Value,
            p.MeleeAccuracy ?? 0, p.RangedAccuracy ?? 0, p.Dodge!.Value, p.Damage!.Trim(),
            p.Ap ?? CombatProfile.DefaultMaxAp,
            p.Abilities?.Select(id => id.Trim()).ToList() ?? [])
        {
            ConditionResist = p.ConditionResist ?? 0,
        };
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
        public List<string>? Abilities { get; set; }
        public int? ConditionResist { get; set; }
    }
}

/// <summary>Maps stored profile values to the rules engine and to wire DTOs.</summary>
internal static class CombatProfileMapping
{
    /// <summary>Rows from before 3B.1 have no profile: they get the 3A placeholder values.</summary>
    /// <param name="combatantType">"player" or "monster": decides <see cref="CanBeDowned"/> unless the values override it.</param>
    public static CombatProfile ToRules(CombatProfileValues? v, string combatantType = "monster") => (v is null
        ? CombatProfile.Default
        : new CombatProfile(v.MaxHp, v.MovementRange, v.Speed, v.MeleeAccuracy, v.RangedAccuracy, v.Dodge,
            DamageDie.Parse(v.BaseDamage), MaxAp(v))) with
        {
            CanBeDowned = CanBeDowned(v, combatantType),
            Abilities = v?.Abilities?.Select(AbilityMapping.ToRules).ToList() ?? [],
            ConditionResist = ConditionResist(v),
        };

    /// <summary>Rows from before 3B.4 PR 2 have no ConditionResist: 0.</summary>
    public static int ConditionResist(CombatProfileValues? v) => v?.ConditionResist ?? 0;

    /// <summary>
    /// Goes Downed at 0 HP instead of dying (3B.5): party combatants do and monsters don't, unless
    /// the stored values (from a battle preset's "downs") say otherwise.
    /// </summary>
    public static bool CanBeDowned(CombatProfileValues? v, string combatantType) =>
        v?.CanBeDowned ?? combatantType == "player";

    public static int MaxHp(CombatProfileValues? v) => v?.MaxHp ?? CombatProfile.Default.MaxHp;
    public static int MovementRange(CombatProfileValues? v) => v?.MovementRange ?? CombatProfile.Default.MovementRange;
    /// <summary>Rows from before 3B.2 have no MaxAp: they get the default.</summary>
    public static int MaxAp(CombatProfileValues? v) => v?.MaxAp ?? CombatProfile.DefaultMaxAp;
}
