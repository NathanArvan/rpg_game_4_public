using RpgGame.Domain.Entities;
using RpgGame.Rules;
using System.Text.Json;

namespace RpgGame.Api.Abilities;

/// <summary>
/// Loads and validates the ability definitions in <c>Abilities/abilities.json</c> once (3B.3,
/// docs/design/ABILITIES.md). A broken file stops startup with a message naming the ability, like
/// <see cref="CombatProfiles.CombatProfileCatalog"/>. Profiles and battle presets list abilities by
/// id; each combatant stores full copies (<see cref="AbilityValues"/>).
/// </summary>
public sealed class AbilityCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Allowed ranges for an ability's numbers.</summary>
    public const int MaxApCost = 10, MaxCooldown = 10, MaxRange = 30;

    /// <summary>Target kinds the resolver supports so far. "self" arrives with movement abilities (3B.3 PR 3); "cell" in 4.1.</summary>
    private static readonly HashSet<string> SupportedTargetKinds = ["combatant", "combatants", "auto"];

    /// <summary>Most targets a "combatants" ability may pick.</summary>
    public const int MaxTargetCount = 6;

    /// <summary>Resonance types (MAGIC_SYSTEM.md), allowed as keys of "resonanceScaling".</summary>
    private static readonly HashSet<string> ResonanceTypes =
        ["flame", "stone", "water", "metal", "wood", "wind", "holy", "unholy", "death", "life", "shadow", "order", "chaos"];

    private readonly Dictionary<string, AbilityValues> _byId;

    public AbilityCatalog(IReadOnlyList<AbilityValues> abilities)
    {
        _byId = new Dictionary<string, AbilityValues>(StringComparer.Ordinal);
        foreach (var a in abilities)
            if (!_byId.TryAdd(a.Id, a))
                throw new InvalidDataException($"Two abilities use the id '{a.Id}'.");
        All = abilities;
    }

    public static AbilityCatalog Empty { get; } = new([]);

    public static AbilityCatalog FromFile(string path) => new(Load(path));

    public static AbilityCatalog FromAppDirectory() =>
        FromFile(Path.Combine(AppContext.BaseDirectory, "Abilities", "abilities.json"));

    /// <summary>Abilities in file order.</summary>
    public IReadOnlyList<AbilityValues> All { get; }

    public AbilityValues? Find(string? id) => id is null ? null : _byId.GetValueOrDefault(id);

    /// <summary>
    /// Fresh copies of the named abilities, for a loadout. <paramref name="fail"/> is called with a
    /// message for an unknown id (it should throw).
    /// </summary>
    public List<AbilityValues> Resolve(IEnumerable<string> ids, Func<string, Exception> fail) =>
        ids.Select(id => Find(id)?.Copy() ?? throw fail($"unknown ability '{id}'")).ToList();

    // ----- Loading & validation -----

    internal static List<AbilityValues> Load(string path)
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
            throw new InvalidDataException($"Abilities {file}: invalid JSON ({ex.Message})", ex);
        }

        return (dto.Abilities ?? []).Select(a => Validate(a, file)).ToList();
    }

    private static AbilityValues Validate(AbilityDto a, string file)
    {
        var who = string.IsNullOrWhiteSpace(a.Id) ? "an ability" : a.Id;
        string Fail(string message) => throw new InvalidDataException($"Abilities {file}: {who}: {message}");

        if (string.IsNullOrWhiteSpace(a.Id)) Fail("'id' is required");
        if (string.IsNullOrWhiteSpace(a.Name)) Fail("'name' is required");
        if (a.Ap is not (>= 1 and <= MaxApCost)) Fail($"'ap' must be between 1 and {MaxApCost}");
        if (a.Cooldown is < 0 or > MaxCooldown) Fail($"'cooldown' must be between 0 and {MaxCooldown}");
        if (a.Charges is not null) Fail("'charges' isn't supported yet");
        if (a.Activation is not (null or "active")) Fail("only \"active\" abilities are supported yet ('activation')");

        var target = a.Target ?? throw new InvalidDataException($"Abilities {file}: {who}: 'target' is required");
        var kind = target.Kind?.Trim().ToLowerInvariant() ?? "";
        if (!SupportedTargetKinds.Contains(kind))
            Fail($"target kind '{target.Kind}' isn't supported yet (use \"combatant\", \"combatants\" or \"auto\")");
        var filter = target.Filter?.Trim().ToLowerInvariant() ?? "";
        if (filter is not ("enemy" or "ally" or "any" or "downed_ally"))
            Fail("target 'filter' must be \"enemy\", \"ally\", \"any\" or \"downed_ally\"");
        if (target.Range is not (>= 1 and <= MaxRange)) Fail($"target 'range' must be between 1 and {MaxRange}");
        if (target.Count is not null && kind is not ("combatants" or "auto")) Fail("target 'count' only applies to \"combatants\" and \"auto\"");
        if (kind == "combatants" && target.Count is not (>= 1 and <= MaxTargetCount))
            Fail($"a \"combatants\" ability needs a target 'count' between 1 and {MaxTargetCount}");
        if (kind == "auto" && target.Count is < 1) Fail("target 'count' must be at least 1 (leave it out for no limit)");
        if (target.IncludeSelf == true && filter == "enemy") Fail("target 'includeSelf' doesn't apply to an \"enemy\" filter");

        if (a.Effects is not { Count: > 0 }) Fail("'effects' must list at least one effect");
        var effects = a.Effects!.Select((e, i) => ValidateEffect(e, $"effect {i + 1}", Fail)).ToList();
        if (filter == "enemy" && effects.Any(e => e.Kind == "heal")) Fail("a heal can't target enemies (use an \"ally\" or \"any\" filter)");

        ValidateResonanceScaling(a.ResonanceScaling, Fail);

        return new AbilityValues
        {
            Id = a.Id!.Trim(),
            Name = a.Name!.Trim(),
            Description = a.Description?.Trim() ?? "",
            ApCost = a.Ap!.Value,
            Cooldown = a.Cooldown ?? 0,
            Tags = (a.Tags ?? []).Select(t => t.Trim().ToLowerInvariant()).ToList(),
            Target = new AbilityTargetValues
            {
                Kind = kind, Filter = filter, Range = target.Range!.Value, Count = target.Count,
                IncludeSelf = target.IncludeSelf ?? false,
            },
            Effects = effects,
        };
    }

    private static AbilityEffectValues ValidateEffect(EffectDto e, string which, Func<string, string> fail)
    {
        switch (e.Kind?.Trim().ToLowerInvariant())
        {
            case "attack":
                var roll = e.Roll?.Trim().ToLowerInvariant() ?? "";
                if (roll is not ("melee" or "ranged" or "none")) fail($"{which}: 'roll' must be \"melee\", \"ranged\" or \"none\"");
                var die = e.Damage?.Die?.Trim().ToLowerInvariant() ?? "weapon";
                if (die != "weapon")
                {
                    try { DamageDie.Parse(die); }
                    catch (FormatException) { fail($"{which}: damage 'die' must be \"weapon\" or a die like \"d10\""); }
                }
                if (e.Damage?.Multiplier is < 1) fail($"{which}: damage 'multiplier' must be at least 1");
                return new AbilityEffectValues
                {
                    Kind = "attack",
                    Roll = roll,
                    AccuracyBonus = e.AccuracyBonus ?? 0,
                    Damage = new AbilityDamageValues
                    {
                        Die = die, Bonus = e.Damage?.Bonus ?? 0, Multiplier = e.Damage?.Multiplier ?? 1,
                    },
                };
            case "heal":
                var healDie = e.Amount?.Die?.Trim().ToLowerInvariant();
                if (healDie is not null)
                {
                    try { DamageDie.Parse(healDie); }
                    catch (FormatException) { fail($"{which}: amount 'die' must be a die like \"d6\" (or left out)"); }
                }
                var healBonus = e.Amount?.Bonus ?? 0;
                if (healBonus < 0) fail($"{which}: amount 'bonus' can't be negative");
                if (healDie is null && healBonus == 0) fail($"{which}: a heal needs an amount (a 'die', a 'bonus' or both)");
                return new AbilityEffectValues
                {
                    Kind = "heal",
                    Amount = new AbilityAmountValues { Die = healDie, Bonus = healBonus },
                    CanRevive = e.CanRevive ?? true,
                };
            default:
                fail($"{which}: unknown effect kind '{e.Kind}' (supported: \"attack\", \"heal\")");
                return null!;
        }
    }

    /// <summary>
    /// "resonanceScaling" (MAGIC_SYSTEM.md) is checked here so abilities can be authored with it now,
    /// but it isn't stored or applied until 3M.5 (ley points & resonance).
    /// </summary>
    private static void ValidateResonanceScaling(Dictionary<string, ResonanceScalingDto>? scaling, Func<string, string> fail)
    {
        foreach (var (type, value) in scaling ?? [])
        {
            if (!ResonanceTypes.Contains(type)) fail($"resonanceScaling: '{type}' isn't a resonance type");
            foreach (var (level, _) in value.ThresholdEffects ?? [])
                if (!int.TryParse(level, out var n) || n < 1)
                    fail($"resonanceScaling.{type}.thresholdEffects: '{level}' must be a resonance level (1 or more)");
        }
    }

    private sealed class FileDto
    {
        public List<AbilityDto>? Abilities { get; set; }
    }

    private sealed class AbilityDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? Ap { get; set; }
        public int? Cooldown { get; set; }
        public int? Charges { get; set; }
        public string? Activation { get; set; }
        public List<string>? Tags { get; set; }
        public TargetDto? Target { get; set; }
        public List<EffectDto>? Effects { get; set; }
        public Dictionary<string, ResonanceScalingDto>? ResonanceScaling { get; set; }
    }

    private sealed class TargetDto
    {
        public string? Kind { get; set; }
        public string? Filter { get; set; }
        public int? Range { get; set; }
        public int? Count { get; set; }
        public bool? IncludeSelf { get; set; }
    }

    private sealed class EffectDto
    {
        public string? Kind { get; set; }
        public string? Roll { get; set; }
        public int? AccuracyBonus { get; set; }
        public DamageDto? Damage { get; set; }
        public AmountDto? Amount { get; set; }
        public bool? CanRevive { get; set; }
    }

    private sealed class AmountDto
    {
        public string? Die { get; set; }
        public int? Bonus { get; set; }
    }

    private sealed class DamageDto
    {
        public string? Die { get; set; }
        public int? Bonus { get; set; }
        public int? Multiplier { get; set; }
    }

    private sealed class ResonanceScalingDto
    {
        public int? DamagePerLevel { get; set; }
        public int? HealingPerLevel { get; set; }
        public Dictionary<string, string>? ThresholdEffects { get; set; }
    }
}

/// <summary>Maps stored ability values to the rules engine.</summary>
internal static class AbilityMapping
{
    public static AbilityDefinition ToRules(AbilityValues a) => new(
        a.Id,
        a.Name,
        a.ApCost,
        a.Cooldown,
        a.Tags,
        new Targeting(ParseKind(a.Target.Kind), ParseFilter(a.Target.Filter), a.Target.Range, a.Target.Count, a.Target.IncludeSelf),
        a.Effects.Select(ToRules).ToList());

    private static AbilityEffect ToRules(AbilityEffectValues e) => e.Kind switch
    {
        "attack" => new AttackEffect(
            e.Roll switch { "ranged" => AttackKind.Ranged, "none" => AttackKind.None, _ => AttackKind.Melee },
            new DamageSpec(
                e.Damage is null || e.Damage.Die == "weapon" ? null : DamageDie.Parse(e.Damage.Die),
                e.Damage?.Bonus ?? 0,
                e.Damage?.Multiplier ?? 1),
            e.AccuracyBonus),
        "heal" => new HealEffect(
            e.Amount?.Die is { } die ? DamageDie.Parse(die) : null,
            e.Amount?.Bonus ?? 0,
            e.CanRevive),
        _ => throw new InvalidDataException($"Unknown ability effect kind '{e.Kind}'."),
    };

    private static TargetKind ParseKind(string kind) => kind switch
    {
        "self" => TargetKind.Self,
        "combatant" => TargetKind.Combatant,
        "combatants" => TargetKind.Combatants,
        "auto" => TargetKind.Auto,
        "cell" => TargetKind.Cell,
        _ => throw new InvalidDataException($"Unknown target kind '{kind}'."),
    };

    private static TargetFilter ParseFilter(string filter) => filter switch
    {
        "enemy" => TargetFilter.Enemy,
        "ally" => TargetFilter.Ally,
        "any" => TargetFilter.Any,
        "downed_ally" => TargetFilter.DownedAlly,
        _ => throw new InvalidDataException($"Unknown target filter '{filter}'."),
    };
}
