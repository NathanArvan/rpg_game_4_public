using RpgGame.Domain.Entities;
using RpgGame.Rules;
using System.Text.Json;

namespace RpgGame.Api.Conditions;

/// <summary>
/// Loads and validates the condition definitions in <c>Conditions/conditions.json</c> once (3B.4,
/// docs/roadmap/SPRINT_3B4.md). A broken file stops startup with a message naming the condition, like
/// <see cref="Abilities.AbilityCatalog"/>. Abilities name conditions by id in their "apply_condition"
/// effects; <see cref="Abilities.AbilityCatalog"/> copies the definition into the effect.
/// </summary>
public sealed class ConditionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Allowed ranges for a condition's numbers.</summary>
    public const int MaxModifier = 20, MaxPeriodic = 20;

    private readonly Dictionary<string, ConditionValues> _byId;

    public ConditionCatalog(IReadOnlyList<ConditionValues> conditions)
    {
        _byId = new Dictionary<string, ConditionValues>(StringComparer.Ordinal);
        foreach (var c in conditions)
        {
            if (c.Id == RulesConfig.GuardedId)
                throw new InvalidDataException($"The condition id '{RulesConfig.GuardedId}' is built in (Defend); don't list it.");
            if (!_byId.TryAdd(c.Id, c))
                throw new InvalidDataException($"Two conditions use the id '{c.Id}'.");
        }
        All = conditions;
    }

    public static ConditionCatalog Empty { get; } = new([]);

    public static ConditionCatalog FromFile(string path) => new(Load(path));

    public static ConditionCatalog FromAppDirectory() =>
        FromFile(Path.Combine(AppContext.BaseDirectory, "Conditions", "conditions.json"));

    /// <summary>Conditions in file order (Guarded, which is built in, isn't among them).</summary>
    public IReadOnlyList<ConditionValues> All { get; }

    public ConditionValues? Find(string? id) => id is null ? null : _byId.GetValueOrDefault(id);

    /// <summary>The built-in Guarded condition (Defend's +3 Dodge), in the stored shape, for display.</summary>
    public static ConditionValues Guarded { get; } = ConditionMapping.FromRules(
        RulesConfig.Default.GuardedCondition, "Defending: harder to hit until the end of the round.");

    // ----- Loading & validation -----

    internal static List<ConditionValues> Load(string path)
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
            throw new InvalidDataException($"Conditions {file}: invalid JSON ({ex.Message})", ex);
        }

        return (dto.Conditions ?? []).Select(c => Validate(c, file)).ToList();
    }

    private static ConditionValues Validate(ConditionDto c, string file)
    {
        var who = string.IsNullOrWhiteSpace(c.Id) ? "a condition" : c.Id;
        string Fail(string message) => throw new InvalidDataException($"Conditions {file}: {who}: {message}");
        void Range(int? value, string name, int max)
        {
            if (value is { } v && (v < -max || v > max)) Fail($"modifier '{name}' must be between -{max} and {max}");
        }

        if (string.IsNullOrWhiteSpace(c.Id)) Fail("'id' is required");
        if (string.IsNullOrWhiteSpace(c.Name)) Fail("'name' is required");
        if (c.Harmful is null) Fail("'harmful' is required (true for a debuff, false for a buff)");
        var m = c.Modifiers ?? throw new InvalidDataException($"Conditions {file}: {who}: 'modifiers' is required");

        Range(m.Accuracy, "accuracy", MaxModifier);
        Range(m.Dodge, "dodge", MaxModifier);
        Range(m.DamageDealt, "damageDealt", MaxModifier);
        Range(m.Movement, "movement", MaxModifier);
        Range(m.Ap, "ap", MaxModifier);
        var attackKind = m.AttackKind?.Trim().ToLowerInvariant() ?? "any";
        if (attackKind is not ("any" or "melee" or "ranged")) Fail("modifier 'attackKind' must be \"any\", \"melee\" or \"ranged\"");
        if (attackKind != "any" && (m.Accuracy ?? 0) == 0 && (m.Dodge ?? 0) == 0)
            Fail("modifier 'attackKind' only applies with 'accuracy' or 'dodge'");

        DamageTakenValues? taken = null;
        if (m.DamageTaken is { } t)
        {
            if (t.Percent is < -100 or > 100) Fail("damageTaken 'percent' must be between -100 and 100");
            Range(t.Flat, "damageTaken.flat", MaxModifier);
            if ((t.Percent ?? 0) == 0 && (t.Flat ?? 0) == 0) Fail("damageTaken needs a 'percent' or a 'flat' amount");
            taken = new DamageTakenValues
            {
                Percent = t.Percent ?? 0, Flat = t.Flat ?? 0, Tags = Tags(t.Tags),
            };
        }

        PeriodicValues? periodic = null;
        if (m.Periodic is { } p)
        {
            var kind = p.Kind?.Trim().ToLowerInvariant() ?? "";
            if (kind is not ("damage" or "heal")) Fail("periodic 'kind' must be \"damage\" or \"heal\"");
            var die = p.Die?.Trim().ToLowerInvariant();
            if (die is not null)
            {
                try { DamageDie.Parse(die); }
                catch (FormatException) { Fail("periodic 'die' must be a die like \"d4\" (or left out)"); }
            }
            if (p.Bonus is < 0 or > MaxPeriodic) Fail($"periodic 'bonus' must be between 0 and {MaxPeriodic}");
            if (die is null && (p.Bonus ?? 0) == 0) Fail("periodic needs an amount (a 'die', a 'bonus' or both)");
            periodic = new PeriodicValues { Kind = kind, Die = die, Bonus = p.Bonus ?? 0 };
        }

        var blocks = (m.Blocks ?? []).Select(b => b.Trim().ToLowerInvariant()).Distinct().ToList();
        foreach (var b in blocks)
            if (b is not ("all" or "move")) Fail($"modifier 'blocks' can only list \"all\" and \"move\" (got '{b}')");

        var values = new ModifierValues
        {
            Accuracy = m.Accuracy ?? 0,
            Dodge = m.Dodge ?? 0,
            AttackKind = attackKind,
            DamageDealt = m.DamageDealt ?? 0,
            DamageTaken = taken,
            Movement = m.Movement ?? 0,
            Ap = m.Ap ?? 0,
            Periodic = periodic,
            Blocks = blocks,
        };
        if (values is { Accuracy: 0, Dodge: 0, DamageDealt: 0, DamageTaken: null, Movement: 0, Ap: 0, Periodic: null, Blocks.Count: 0 })
            Fail("'modifiers' must change something");

        return new ConditionValues
        {
            Id = c.Id!.Trim(),
            Name = c.Name!.Trim(),
            Description = c.Description?.Trim() ?? "",
            Harmful = c.Harmful!.Value,
            Tags = Tags(c.Tags),
            Modifiers = values,
        };
    }

    private static List<string> Tags(List<string>? tags) =>
        (tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToList();

    private sealed class FileDto
    {
        public List<ConditionDto>? Conditions { get; set; }
    }

    private sealed class ConditionDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool? Harmful { get; set; }
        public List<string>? Tags { get; set; }
        public ModifiersDto? Modifiers { get; set; }
    }

    private sealed class ModifiersDto
    {
        public int? Accuracy { get; set; }
        public int? Dodge { get; set; }
        public string? AttackKind { get; set; }
        public int? DamageDealt { get; set; }
        public DamageTakenDto? DamageTaken { get; set; }
        public int? Movement { get; set; }
        public int? Ap { get; set; }
        public PeriodicDto? Periodic { get; set; }
        public List<string>? Blocks { get; set; }
    }

    private sealed class DamageTakenDto
    {
        public int? Percent { get; set; }
        public int? Flat { get; set; }
        public List<string>? Tags { get; set; }
    }

    private sealed class PeriodicDto
    {
        public string? Kind { get; set; }
        public string? Die { get; set; }
        public int? Bonus { get; set; }
    }
}

/// <summary>Maps stored condition values to the rules engine and back.</summary>
internal static class ConditionMapping
{
    public static ConditionDefinition ToRules(ConditionValues c) => new(
        c.Id,
        c.Name,
        c.Harmful,
        c.Tags,
        new Modifiers(
            Accuracy: c.Modifiers.Accuracy,
            Dodge: c.Modifiers.Dodge,
            AttackKind: c.Modifiers.AttackKind switch
            {
                "melee" => AttackKindFilter.Melee,
                "ranged" => AttackKindFilter.Ranged,
                _ => AttackKindFilter.Any,
            },
            DamageDealt: c.Modifiers.DamageDealt,
            DamageTaken: c.Modifiers.DamageTaken is { } t ? new DamageTakenModifier(t.Percent, t.Flat, t.Tags) : null,
            Movement: c.Modifiers.Movement,
            Ap: c.Modifiers.Ap,
            Periodic: c.Modifiers.Periodic is { } p
                ? new PeriodicEffect(p.Kind == "heal" ? PeriodicKind.Heal : PeriodicKind.Damage,
                    p.Die is { } die ? DamageDie.Parse(die) : null, p.Bonus)
                : null,
            Blocks: c.Modifiers.Blocks.Select(b => b == "move" ? BlockKind.Move : BlockKind.All).ToList()));

    public static ActiveCondition ToRules(ActiveConditionValues a) => new(ToRules(a.Condition), a.RoundsLeft, a.SourceId);

    /// <summary>
    /// The stored shape of a rules definition. The resolver's conditions carry no description, so the
    /// caller gives one (or the empty string).
    /// </summary>
    public static ConditionValues FromRules(ConditionDefinition c, string description = "") => new()
    {
        Id = c.Id,
        Name = c.Name,
        Description = description,
        Harmful = c.Harmful,
        Tags = [.. c.Tags],
        Modifiers = new ModifierValues
        {
            Accuracy = c.Modifiers.Accuracy,
            Dodge = c.Modifiers.Dodge,
            AttackKind = c.Modifiers.AttackKind.ToString().ToLowerInvariant(),
            DamageDealt = c.Modifiers.DamageDealt,
            DamageTaken = c.Modifiers.DamageTaken is { } t
                ? new DamageTakenValues { Percent = t.Percent, Flat = t.Flat, Tags = [.. t.Tags] }
                : null,
            Movement = c.Modifiers.Movement,
            Ap = c.Modifiers.Ap,
            Periodic = c.Modifiers.Periodic is { } p
                ? new PeriodicValues { Kind = p.Kind == PeriodicKind.Heal ? "heal" : "damage", Die = p.Die?.ToString().Replace("1d", "d"), Bonus = p.Bonus }
                : null,
            Blocks = c.Modifiers.Blocks.Select(b => b.ToString().ToLowerInvariant()).ToList(),
        },
    };
}
