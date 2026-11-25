using RpgGame.Api.Conditions;
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

    /// <summary>Target kinds the resolver supports so far; "cell" arrives in 4.1.</summary>
    private static readonly HashSet<string> SupportedTargetKinds = ["self", "combatant", "combatants", "auto", "trigger"];

    /// <summary>Prepared-reaction triggers (3B.6): movement triggers (PR 2) and attack triggers (PR 3).</summary>
    private static readonly HashSet<string> SupportedTriggers = ["enemy_enters_reach", "enemy_moves_within", "attacked", "ally_attacked"];

    /// <summary>
    /// The one effect each prepared trigger resolves (3B.6): movement triggers attack whoever set them off; "attacked"
    /// raises Dodge ("modify_incoming", Parry); "ally_attacked" takes the attack ("redirect", Intercept).
    /// </summary>
    private static readonly Dictionary<string, string> TriggerEffect = new()
    {
        ["enemy_enters_reach"] = "attack",
        ["enemy_moves_within"] = "attack",
        ["attacked"] = "modify_incoming",
        ["ally_attacked"] = "redirect",
    };

    /// <summary>Most Dodge a "modify_incoming" effect may add.</summary>
    public const int MaxReactionDodge = 10;

    /// <summary>Most targets a "combatants" ability may pick.</summary>
    public const int MaxTargetCount = 6;

    /// <summary>Resonance types (MAGIC_SYSTEM.md), allowed as keys of "resonanceScaling".</summary>
    private static readonly HashSet<string> ResonanceTypes =
        ["flame", "stone", "water", "metal", "wood", "wind", "holy", "unholy", "death", "life", "shadow", "order", "chaos"];

    private readonly Dictionary<string, AbilityValues> _byId;

    public AbilityCatalog(IReadOnlyList<AbilityValues> abilities, ConditionCatalog? conditions = null)
    {
        Conditions = conditions ?? ConditionCatalog.Empty;
        _byId = new Dictionary<string, AbilityValues>(StringComparer.Ordinal);
        foreach (var a in abilities)
            if (!_byId.TryAdd(a.Id, a))
                throw new InvalidDataException($"Two abilities use the id '{a.Id}'.");
        All = abilities;
    }

    public static AbilityCatalog Empty { get; } = new([]);

    /// <param name="conditions">The conditions "apply_condition" effects may name (3B.4); none if left out.</param>
    public static AbilityCatalog FromFile(string path, ConditionCatalog? conditions = null)
    {
        conditions ??= ConditionCatalog.Empty;
        return new(Load(path, conditions), conditions);
    }

    /// <summary>The shipped abilities, with the shipped conditions (<c>Conditions/conditions.json</c>).</summary>
    public static AbilityCatalog FromAppDirectory() =>
        FromFile(Path.Combine(AppContext.BaseDirectory, "Abilities", "abilities.json"), ConditionCatalog.FromAppDirectory());

    /// <summary>The conditions this catalog's abilities were checked against (3B.4).</summary>
    public ConditionCatalog Conditions { get; }

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

    internal static List<AbilityValues> Load(string path, ConditionCatalog? conditions = null)
    {
        conditions ??= ConditionCatalog.Empty;
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

        return (dto.Abilities ?? []).Select(a => Validate(a, file, conditions)).ToList();
    }

    private static AbilityValues Validate(AbilityDto a, string file, ConditionCatalog conditions)
    {
        var who = string.IsNullOrWhiteSpace(a.Id) ? "an ability" : a.Id;
        string Fail(string message) => throw new InvalidDataException($"Abilities {file}: {who}: {message}");

        if (string.IsNullOrWhiteSpace(a.Id)) Fail("'id' is required");
        if (string.IsNullOrWhiteSpace(a.Name)) Fail("'name' is required");
        if (a.Ap is not (>= 1 and <= MaxApCost)) Fail($"'ap' must be between 1 and {MaxApCost}");
        if (a.Cooldown is < 0 or > MaxCooldown) Fail($"'cooldown' must be between 0 and {MaxCooldown}");
        if (a.Charges is not null) Fail("'charges' isn't supported yet");
        var activation = a.Activation?.Trim().ToLowerInvariant() ?? "active";
        if (activation is not ("active" or "prepared")) Fail("'activation' must be \"active\" or \"prepared\"");
        bool prepared = activation == "prepared";
        var trigger = ValidateTrigger(a.Trigger, prepared, Fail);

        var target = a.Target ?? throw new InvalidDataException($"Abilities {file}: {who}: 'target' is required");
        var kind = target.Kind?.Trim().ToLowerInvariant() ?? "";
        if (!SupportedTargetKinds.Contains(kind))
            Fail($"target kind '{target.Kind}' isn't supported yet (use \"self\", \"combatant\", \"combatants\", \"auto\" or \"trigger\")");
        bool self = kind == "self";
        bool onTrigger = kind == "trigger";
        if (onTrigger && !prepared) Fail("target kind \"trigger\" is only for prepared abilities");
        if (prepared && !(onTrigger || self)) Fail("a prepared ability targets \"trigger\" (whoever set it off) or \"self\"");
        // A "self" ability needs no filter or range: it only ever affects its caster. A "trigger" one hits whoever set it
        // off, an enemy for every trigger built so far.
        var filter = target.Filter?.Trim().ToLowerInvariant() ?? (self ? "ally" : onTrigger ? "enemy" : "");
        if (filter is not ("enemy" or "ally" or "any" or "downed_ally"))
            Fail("target 'filter' must be \"enemy\", \"ally\", \"any\" or \"downed_ally\"");
        if (onTrigger && target.Range is not null) Fail("a \"trigger\" target takes no 'range' (the trigger decides who)");
        if (!self && !onTrigger && target.Range is not (>= 1 and <= MaxRange)) Fail($"target 'range' must be between 1 and {MaxRange}");
        if (target.Count is not null && kind is not ("combatants" or "auto")) Fail("target 'count' only applies to \"combatants\" and \"auto\"");
        if (kind == "combatants" && target.Count is not (>= 1 and <= MaxTargetCount))
            Fail($"a \"combatants\" ability needs a target 'count' between 1 and {MaxTargetCount}");
        if (kind == "auto" && target.Count is < 1) Fail("target 'count' must be at least 1 (leave it out for no limit)");
        if (target.IncludeSelf == true && filter == "enemy") Fail("target 'includeSelf' doesn't apply to an \"enemy\" filter");

        if (a.Effects is not { Count: > 0 }) Fail("'effects' must list at least one effect");
        bool attacks = a.Effects!.Any(e => e.Kind?.Trim().ToLowerInvariant() == "attack");
        var effects = a.Effects!.Select((e, i) => ValidateEffect(e, $"effect {i + 1}", Fail, conditions, attacks)).ToList();
        if (filter == "enemy" && effects.Any(e => e.Kind == "heal")) Fail("a heal can't target enemies (use an \"ally\" or \"any\" filter)");
        if (self && filter != "ally") Fail("a \"self\" ability doesn't take a target 'filter'");
        if (self && effects.Any(e => e.Kind == "attack")) Fail("a \"self\" ability can't attack (it would hit its caster)");
        if (prepared && trigger is not null && TriggerEffect.GetValueOrDefault(trigger.Kind) is { } wanted)
        {
            if (effects.FirstOrDefault(e => e.Kind != wanted) is { } other)
                Fail($"a \"{trigger.Kind}\" reaction's effects can only be \"{wanted}\" (not \"{other.Kind}\")");
            if (wanted != "attack" && effects.Count != 1) Fail($"a \"{trigger.Kind}\" reaction takes one \"{wanted}\" effect");
            if (wanted != "attack" && !self) Fail($"a \"{trigger.Kind}\" reaction targets \"self\"");
            if (wanted == "attack" && !onTrigger) Fail($"a \"{trigger.Kind}\" reaction targets \"trigger\" (whoever set it off)");
        }
        if (!prepared && effects.FirstOrDefault(e => e.Kind is "modify_incoming" or "redirect") is { } reactionOnly)
            Fail($"a \"{reactionOnly.Kind}\" effect is only for prepared abilities");
        var moves = effects.Where(e => e.Kind == "move").ToList();
        if (moves.Count > 1) Fail("an ability can move its caster only once (one \"move\" effect)");
        if (moves.FirstOrDefault() is { } move)
        {
            if (move.To == "cell" && !self) Fail("a move to a \"cell\" needs a \"self\" target kind");
            if (move.To == "target" && kind != "combatant") Fail("a move to the \"target\" needs a \"combatant\" target kind");
        }
        if (moves.Count == 0 && effects.Any(e => e.Damage?.BonusIfMoved is not null))
            Fail("damage 'bonusIfMoved' needs a \"move\" effect in the same ability");

        if (effects.Any(e => e.Kind == "apply_condition" && e.RequiresHit) && !attacks)
            Fail("an \"apply_condition\" with 'requiresHit' needs an \"attack\" effect in the same ability");
        var harmful = effects.Where(e => e.Condition is { Harmful: true }).Select(e => e.Condition!.Id).ToList();
        var helpful = effects.Where(e => e.Condition is { Harmful: false }).Select(e => e.Condition!.Id).ToList();
        if (self && harmful.Count > 0) Fail($"a \"self\" ability can't apply a harmful condition ('{harmful[0]}')");
        if (filter is "ally" or "downed_ally" && harmful.Count > 0) Fail($"an \"ally\" ability can't apply a harmful condition ('{harmful[0]}')");
        if (filter == "enemy" && helpful.Count > 0) Fail($"an \"enemy\" ability can't apply a beneficial condition ('{helpful[0]}')");

        ValidateResonanceScaling(a.ResonanceScaling, Fail, conditions);

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
                Kind = kind, Filter = filter, Range = self || onTrigger ? 0 : target.Range!.Value, Count = target.Count,
                IncludeSelf = target.IncludeSelf ?? false,
            },
            Effects = effects,
            Activation = activation,
            Trigger = trigger,
        };
    }

    /// <summary>
    /// A prepared ability's trigger (3B.6, SPRINT_3B6 X6): required on a prepared ability and refused on an active one; a
    /// known kind; a 'range' for "enemy_moves_within" (and nowhere else yet).
    /// </summary>
    private static AbilityTriggerValues? ValidateTrigger(TriggerDto? trigger, bool prepared, Func<string, string> fail)
    {
        if (!prepared)
        {
            if (trigger is not null) fail("only a prepared ability takes a 'trigger' (set \"activation\": \"prepared\")");
            return null;
        }
        if (trigger is null) { fail("a prepared ability needs a 'trigger'"); return null; }
        var kind = trigger.Kind?.Trim().ToLowerInvariant() ?? "";
        if (!SupportedTriggers.Contains(kind))
            fail($"unknown trigger '{trigger.Kind}' (use \"enemy_enters_reach\", \"enemy_moves_within\", \"attacked\" or \"ally_attacked\")");
        if (kind == "enemy_moves_within" && trigger.Range is not (>= 1 and <= MaxRange))
            fail($"trigger \"enemy_moves_within\" needs a 'range' between 1 and {MaxRange}");
        if (kind == "ally_attacked" && trigger.Range is not (null or (>= 1 and <= MaxRange)))
            fail($"trigger \"ally_attacked\" 'range' must be between 1 and {MaxRange} (leave it out for melee reach)");
        if (kind is "enemy_enters_reach" or "attacked" && trigger.Range is not null)
            fail($"trigger \"{kind}\" takes no 'range'");
        var attackKind = trigger.AttackKind?.Trim().ToLowerInvariant();
        if (attackKind is not null && kind != "attacked")
            fail("trigger 'attackKind' only applies to \"attacked\"");
        if (attackKind is not (null or "melee" or "ranged"))
            fail("trigger 'attackKind' must be \"melee\" or \"ranged\" (leave it out for any attack)");
        return new AbilityTriggerValues { Kind = kind, Range = trigger.Range, AttackKind = attackKind };
    }

    private static AbilityEffectValues ValidateEffect(
        EffectDto e, string which, Func<string, string> fail, ConditionCatalog conditions, bool abilityAttacks)
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
                var ifMoved = e.Damage?.BonusIfMoved;
                if (ifMoved is not null && (ifMoved.MinCells is not (>= 1 and <= MaxRange) || ifMoved.Bonus is not >= 1))
                    fail($"{which}: damage 'bonusIfMoved' needs 'minCells' between 1 and {MaxRange} and a 'bonus' of at least 1");
                return new AbilityEffectValues
                {
                    Kind = "attack",
                    Roll = roll,
                    AccuracyBonus = e.AccuracyBonus ?? 0,
                    Damage = new AbilityDamageValues
                    {
                        Die = die, Bonus = e.Damage?.Bonus ?? 0, Multiplier = e.Damage?.Multiplier ?? 1,
                        BonusIfMoved = ifMoved is null ? null
                            : new AbilityMovedBonusValues { MinCells = ifMoved.MinCells!.Value, Bonus = ifMoved.Bonus!.Value },
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
            case "move":
                var to = e.To?.Trim().ToLowerInvariant() ?? "";
                if (to is not ("cell" or "target")) fail($"{which}: 'to' must be \"cell\" or \"target\"");
                if (e.MaxCells is not (>= 1 and <= MaxRange)) fail($"{which}: 'maxCells' must be between 1 and {MaxRange}");
                if (e.Path is not (null or "walk")) fail($"{which}: only \"walk\" moves are supported yet ('path')");
                return new AbilityEffectValues
                {
                    Kind = "move", To = to, MaxCells = e.MaxCells!.Value, UsesMovementBudget = e.UsesMovementBudget ?? true,
                };
            case "apply_condition":
                var id = e.Condition?.Trim() ?? "";
                if (id.Length == 0) fail($"{which}: 'condition' is required");
                var condition = conditions.Find(id);
                if (condition is null) fail($"{which}: unknown condition '{id}'");
                if (e.Duration is not (>= 0 and <= MaxCooldown))
                    fail($"{which}: 'duration' must be between 0 and {MaxCooldown} (0 = until the end of the round)");
                return new AbilityEffectValues
                {
                    Kind = "apply_condition",
                    Condition = condition!.Copy(),
                    Duration = e.Duration!.Value,
                    // A condition on an ability that attacks lands only on a hit, unless it says otherwise.
                    RequiresHit = e.RequiresHit ?? abilityAttacks,
                };
            case "modify_incoming":
                if (e.Dodge is not (>= 1 and <= MaxReactionDodge)) fail($"{which}: 'dodge' must be between 1 and {MaxReactionDodge}");
                return new AbilityEffectValues { Kind = "modify_incoming", Dodge = e.Dodge!.Value };
            case "redirect":
                return new AbilityEffectValues { Kind = "redirect" };
            default:
                fail($"{which}: unknown effect kind '{e.Kind}' (supported: \"attack\", \"heal\", \"move\", \"apply_condition\", \"modify_incoming\", \"redirect\")");
                return null!;
        }
    }

    /// <summary>
    /// Threshold effects that aren't conditions (3M.5 / 3D.4 will define them). Every other
    /// <c>thresholdEffects</c> value must name a condition in the <see cref="ConditionCatalog"/> (3B.4 PR 2).
    /// </summary>
    private static readonly HashSet<string> NonConditionThresholdEffects = ["ember"];

    /// <summary>
    /// "resonanceScaling" (MAGIC_SYSTEM.md) is checked here so abilities can be authored with it now,
    /// but it isn't stored or applied until 3M.5 (ley points & resonance). A threshold effect must be a
    /// known condition (or one of the reserved non-condition effects).
    /// </summary>
    private static void ValidateResonanceScaling(
        Dictionary<string, ResonanceScalingDto>? scaling, Func<string, string> fail, ConditionCatalog conditions)
    {
        foreach (var (type, value) in scaling ?? [])
        {
            if (!ResonanceTypes.Contains(type)) fail($"resonanceScaling: '{type}' isn't a resonance type");
            foreach (var (level, effect) in value.ThresholdEffects ?? [])
            {
                if (!int.TryParse(level, out var n) || n < 1)
                    fail($"resonanceScaling.{type}.thresholdEffects: '{level}' must be a resonance level (1 or more)");
                var id = effect?.Trim() ?? "";
                if (!NonConditionThresholdEffects.Contains(id) && conditions.Find(id) is null)
                    fail($"resonanceScaling.{type}.thresholdEffects: '{id}' isn't a condition in conditions.json");
            }
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
        public TriggerDto? Trigger { get; set; }
        public List<string>? Tags { get; set; }
        public TargetDto? Target { get; set; }
        public List<EffectDto>? Effects { get; set; }
        public Dictionary<string, ResonanceScalingDto>? ResonanceScaling { get; set; }
    }

    private sealed class TriggerDto
    {
        public string? Kind { get; set; }
        public int? Range { get; set; }
        public string? AttackKind { get; set; }
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
        public string? To { get; set; }
        public int? MaxCells { get; set; }
        public bool? UsesMovementBudget { get; set; }
        public string? Path { get; set; }
        public string? Condition { get; set; }
        public int? Duration { get; set; }
        public bool? RequiresHit { get; set; }
        public int? Dodge { get; set; }
    }

    private sealed class MovedBonusDto
    {
        public int? MinCells { get; set; }
        public int? Bonus { get; set; }
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
        public MovedBonusDto? BonusIfMoved { get; set; }
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
        a.Effects.Select(ToRules).ToList(),
        Activation: a.Activation == "prepared" ? AbilityActivation.Prepared : AbilityActivation.Active)
    {
        Trigger = a.Trigger is { } t ? new ReactionTrigger(ParseTrigger(t.Kind), t.Range,
            t.AttackKind switch { "melee" => AttackKind.Melee, "ranged" => AttackKind.Ranged, _ => null }) : null,
    };

    private static TriggerKind ParseTrigger(string kind) => kind switch
    {
        "enemy_enters_reach" => TriggerKind.EnemyEntersReach,
        "enemy_moves_within" => TriggerKind.EnemyMovesWithin,
        "attacked" => TriggerKind.Attacked,
        "ally_attacked" => TriggerKind.AllyAttacked,
        _ => throw new InvalidDataException($"Unknown trigger kind '{kind}'."),
    };

    private static AbilityEffect ToRules(AbilityEffectValues e) => e.Kind switch
    {
        "attack" => new AttackEffect(
            e.Roll switch { "ranged" => AttackKind.Ranged, "none" => AttackKind.None, _ => AttackKind.Melee },
            new DamageSpec(
                e.Damage is null || e.Damage.Die == "weapon" ? null : DamageDie.Parse(e.Damage.Die),
                e.Damage?.Bonus ?? 0,
                e.Damage?.Multiplier ?? 1,
                e.Damage?.BonusIfMoved is { } m ? new MovedBonus(m.MinCells, m.Bonus) : null),
            e.AccuracyBonus),
        "move" => new MoveEffect(e.To == "target" ? MoveTo.Target : MoveTo.Cell, e.MaxCells, e.UsesMovementBudget),
        "heal" => new HealEffect(
            e.Amount?.Die is { } die ? DamageDie.Parse(die) : null,
            e.Amount?.Bonus ?? 0,
            e.CanRevive),
        "apply_condition" => new ApplyConditionEffect(
            ConditionMapping.ToRules(e.Condition ?? throw new InvalidDataException("An apply_condition effect has no condition.")),
            e.Duration,
            e.RequiresHit),
        "modify_incoming" => new ModifyIncomingEffect(e.Dodge),
        "redirect" => new RedirectEffect(),
        _ => throw new InvalidDataException($"Unknown ability effect kind '{e.Kind}'."),
    };

    private static TargetKind ParseKind(string kind) => kind switch
    {
        "self" => TargetKind.Self,
        "combatant" => TargetKind.Combatant,
        "combatants" => TargetKind.Combatants,
        "auto" => TargetKind.Auto,
        "cell" => TargetKind.Cell,
        "trigger" => TargetKind.Trigger,
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
