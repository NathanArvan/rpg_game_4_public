using System.Text.Json;
using System.Text.RegularExpressions;
using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

/// <summary>
/// Runs every <c>Scenarios/*.json</c> file through <see cref="RoundResolver"/>.
/// The file format is documented in <c>Scenarios/README.md</c>.
/// </summary>
public partial class ScenarioTests
{
    private static readonly string ScenarioDir = Path.Combine(AppContext.BaseDirectory, "Scenarios");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(ScenarioDir, "*.json").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Scenario(string file)
    {
        var scenario = Load(file);
        var (state, plans, aiPlans, dice, config) = Build(scenario, file);

        var outcome = RoundResolver.Resolve(state, plans, dice, config);

        var failures = Check(scenario.Expect ?? new ExpectDto(), outcome);
        CheckAiPlans(ExpectedAiPlans(scenario.Expect?.Plans, file), aiPlans, failures);
        if (dice.Remaining != 0)
            failures.Add($"dice: {dice.Remaining} scripted roll(s) were never used");

        Assert.True(failures.Count == 0,
            $"{file}: {scenario.Description}\n  - " + string.Join("\n  - ", failures));
    }

    [Fact]
    public void ScenarioFolder_IsNotEmpty() => Assert.NotEmpty(Files());

    // ----- Loading -----

    private static ScenarioDto Load(string file)
    {
        var json = File.ReadAllText(Path.Combine(ScenarioDir, file));
        return JsonSerializer.Deserialize<ScenarioDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"{file}: empty scenario");
    }

    private static (EncounterState, Dictionary<string, IReadOnlyList<PlannedAction>>, Dictionary<string, IReadOnlyList<PlannedAction>>, ScriptedDice, RulesConfig) Build(
        ScenarioDto s, string file)
    {
        if (s.Combatants is not { Count: > 0 })
            throw new InvalidDataException($"{file}: 'combatants' must list at least one combatant");

        var grid = s.Grid ?? [20, 20];
        var abilities = (s.Abilities ?? []).ToDictionary(a => a.Id, a => ToAbility(a, file));
        var combatants = s.Combatants.Select(c =>
        {
            if (string.IsNullOrWhiteSpace(c.Id)) throw new InvalidDataException($"{file}: every combatant needs an 'id'");
            var hp = c.Hp ?? 20;
            var d = CombatProfile.Default;
            var profile = new CombatProfile(
                MaxHp: c.MaxHp ?? Math.Max(hp, 1),
                MovementRange: c.Move ?? d.MovementRange,
                Speed: c.Speed ?? d.Speed,
                MeleeAccuracy: c.MeleeAccuracy ?? d.MeleeAccuracy,
                RangedAccuracy: c.RangedAccuracy ?? d.RangedAccuracy,
                Dodge: c.Dodge ?? d.Dodge,
                BaseDamage: DamageDie.Parse(c.Damage ?? "d8"),
                MaxAp: c.MaxAp ?? d.MaxAp,
                CanBeDowned: c.Downs ?? false)
            {
                Abilities = (c.Abilities ?? []).Select(id => abilities.TryGetValue(id, out var a) ? a
                    : throw new InvalidDataException($"{file}: combatant {c.Id} lists ability '{id}', which isn't in 'abilities'")).ToList(),
            };
            // "status" sets a starting status (3B.5); the older "defeated": true means dead.
            var status = c.Status is { } st ? ParseStatus(st, $"{file}: combatant {c.Id} 'status'")
                : c.Defeated == true ? CombatantStatus.Dead : CombatantStatus.Standing;
            return new CombatantState(
                c.Id,
                c.Name ?? c.Id,
                hp,
                profile,
                c.At is { } at ? Cell(at, $"{file}: combatant {c.Id} 'at'") : null,
                status,
                status == CombatantStatus.Downed ? c.Bleedout ?? RulesConfig.Default.BleedoutRounds : 0)
            {
                Team = c.Side ?? "",
                Cooldowns = c.Cooldowns ?? [],
            };
        }).ToList();

        // JSON object key order is preserved: plan order is meaningful to the resolver.
        var plans = new Dictionary<string, IReadOnlyList<PlannedAction>>();
        var aiIds = new List<string>();
        foreach (var (id, queue) in s.Plans ?? [])
        {
            if (queue.Any(IsAi))
            {
                if (queue.Count != 1)
                    throw new InvalidDataException($"{file}: plan for {id}: \"ai\" must be the only entry");
                aiIds.Add(id);
                plans[id] = []; // placeholder keeps the combatant's place in plan order
                continue;
            }
            plans[id] = queue.Select(text => ParseAction(text, $"{file}: plan for {id}")).ToList();
        }

        var config = s.Config is null
            ? RulesConfig.Default
            : RulesConfig.Default with
            {
                MeleeReach = s.Config.MeleeReach ?? RulesConfig.Default.MeleeReach,
                BleedoutRounds = s.Config.BleedoutRounds ?? RulesConfig.Default.BleedoutRounds,
            };

        var state = new EncounterState(grid[0], grid[1], combatants);

        // "ai" plans are filled in by EnemyAi from the starting state, before the round resolves.
        var aiPlans = new Dictionary<string, IReadOnlyList<PlannedAction>>();
        if (aiIds.Count > 0)
        {
            var teams = s.Combatants.ToDictionary(c => c.Id, c => c.Side ?? "party");
            aiPlans = EnemyAi.PlanRound(state, teams, aiIds, config);
            foreach (var id in aiIds)
                plans[id] = aiPlans.TryGetValue(id, out var queue) ? queue : [];
        }

        return (state, plans, aiPlans, new ScriptedDice(s.Dice ?? []), config);
    }

    private static bool IsAi(string text) => text.Trim().Equals("ai", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, List<string>>? ExpectedAiPlans(Dictionary<string, JsonElement>? raw, string file) =>
        raw?.ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind switch
        {
            JsonValueKind.String => [kv.Value.GetString()!],
            JsonValueKind.Array => kv.Value.EnumerateArray().Select(e => e.GetString()!).ToList(),
            _ => throw new InvalidDataException($"{file}: expect.plans.{kv.Key} must be an action string or a list of them"),
        });

    private static void CheckAiPlans(Dictionary<string, List<string>>? expected, Dictionary<string, IReadOnlyList<PlannedAction>> actual, List<string> failures)
    {
        foreach (var (id, texts) in expected ?? [])
        {
            var want = texts.Select(t => ParseAction(t, $"expect.plans.{id}")).ToList();
            var wanted = string.Join(", ", texts.Select(t => $"\"{t}\""));
            if (!actual.TryGetValue(id, out var got))
                failures.Add($"AI plan for {id}: expected [{wanted}], but {id} had no AI plan");
            else if (!got.SequenceEqual(want))
                failures.Add($"AI plan for {id}: expected [{wanted}], got [{string.Join(", ", got.Select(Describe))}]");
        }
    }

    private static string Describe(PlannedAction a) => a.Type switch
    {
        ActionType.Move => $"\"move {a.TargetCell!.Value.X},{a.TargetCell.Value.Y}\"",
        ActionType.MeleeAttack => $"\"melee {a.TargetCombatantId}\"",
        ActionType.RangedAttack => $"\"ranged {a.TargetCombatantId}\"",
        ActionType.Stabilize => $"\"stabilize {a.TargetCombatantId}\"",
        ActionType.Ability => $"\"ability {a.AbilityId}{(a.TargetCombatantIds is { } ids ? " " + string.Join(",", ids) : a.TargetCombatantId is null ? "" : " " + a.TargetCombatantId)}\"",
        _ => "\"defend\"",
    };

    [GeneratedRegex(@"^\s*move\s+(-?\d+)\s*,\s*(-?\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MoveRegex();

    [GeneratedRegex(@"^\s*(melee|ranged)\s+(\S+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AttackRegex();

    [GeneratedRegex(@"^\s*stabilize\s+(\S+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StabilizeRegex();

    /// <summary>"ability ID", "ability ID TARGET" or "ability ID T1,T2" (several chosen targets) (3B.3).</summary>
    [GeneratedRegex(@"^\s*ability\s+(\S+)(?:\s+(\S+))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AbilityRegex();

    internal static PlannedAction ParseAction(string text, string where)
    {
        if (MoveRegex().Match(text) is { Success: true } m)
            return PlannedAction.Move(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        if (AttackRegex().Match(text) is { Success: true } a)
            return a.Groups[1].Value.Equals("melee", StringComparison.OrdinalIgnoreCase)
                ? PlannedAction.Melee(a.Groups[2].Value)
                : PlannedAction.Ranged(a.Groups[2].Value);
        if (StabilizeRegex().Match(text) is { Success: true } st)
            return PlannedAction.Stabilize(st.Groups[1].Value);
        if (AbilityRegex().Match(text) is { Success: true } ab)
        {
            var target = ab.Groups[2].Success ? ab.Groups[2].Value : null;
            return target is not null && target.Contains(',')
                ? new PlannedAction(ActionType.Ability, AbilityId: ab.Groups[1].Value, TargetCombatantIds: target.Split(','))
                : PlannedAction.Ability(ab.Groups[1].Value, target);
        }
        if (text.Trim().Equals("defend", StringComparison.OrdinalIgnoreCase))
            return PlannedAction.Defend();
        throw new FormatException(
            $"{where}: can't parse action \"{text}\". Use \"move X,Y\", \"melee ID\", \"ranged ID\", \"stabilize ID\", \"ability ABILITY [TARGET[,TARGET…]]\" or \"defend\".");
    }

    /// <summary>An inline ability definition, in the same shape as the server's abilities.json (3B.3).</summary>
    private static AbilityDefinition ToAbility(AbilityDto a, string file)
    {
        var where = $"{file}: ability {a.Id}";
        if (a.Target is null) throw new InvalidDataException($"{where}: 'target' is required");
        var targeting = new Targeting(
            Enum.Parse<TargetKind>(a.Target.Kind ?? "combatant", ignoreCase: true),
            Enum.Parse<TargetFilter>((a.Target.Filter ?? "enemy").Replace("_", ""), ignoreCase: true),
            a.Target.Range ?? 1, a.Target.Count, a.Target.IncludeSelf ?? false);
        var effects = (a.Effects ?? []).Select<EffectDto, AbilityEffect>(e => e.Kind switch
        {
            "attack" => new AttackEffect(
                Enum.Parse<AttackKind>(e.Roll ?? "melee", ignoreCase: true),
                new DamageSpec(
                    e.Damage?.Die is null or "weapon" ? null : DamageDie.Parse(e.Damage.Die),
                    e.Damage?.Bonus ?? 0,
                    e.Damage?.Multiplier ?? 1),
                e.AccuracyBonus ?? 0),
            "heal" => new HealEffect(
                e.Amount?.Die is null ? null : DamageDie.Parse(e.Amount.Die),
                e.Amount?.Bonus ?? 0,
                e.CanRevive ?? true),
            _ => throw new InvalidDataException($"{where}: unknown effect kind '{e.Kind}'"),
        }).ToList();
        return new AbilityDefinition(a.Id, a.Name ?? a.Id, a.Ap ?? 2, a.Cooldown ?? 0, a.Tags ?? [], targeting, effects);
    }

    private static CombatantStatus ParseStatus(string text, string where) =>
        Enum.TryParse<CombatantStatus>(text, ignoreCase: true, out var status)
            ? status
            : throw new InvalidDataException($"{where}: \"{text}\" isn't a status. Use standing, downed, stable or dead.");

    private static GridPosition Cell(int[] xy, string where) => xy.Length == 2
        ? new GridPosition(xy[0], xy[1])
        : throw new InvalidDataException($"{where}: expected [x, y]");

    // ----- Checking -----

    private static List<string> Check(ExpectDto e, RoundOutcome o)
    {
        var failures = new List<string>();
        var final = o.Next.Combatants.ToDictionary(c => c.Id);

        if (e.Ticks is { } ticks && ticks != o.Ticks.Count)
            failures.Add($"ticks: expected {ticks}, got {o.Ticks.Count}");

        foreach (var (id, xy) in e.Positions ?? [])
        {
            var expected = Cell(xy, $"expect.positions.{id}");
            var actual = final.TryGetValue(id, out var c) ? c.Position : null;
            if (actual != expected)
                failures.Add($"position of {id}: expected {expected}, got {actual?.ToString() ?? "none"}");
        }

        foreach (var (id, hp) in e.Hp ?? [])
        {
            var actual = final.TryGetValue(id, out var c) ? c.Hp : (int?)null;
            if (actual != hp)
                failures.Add($"hp of {id}: expected {hp}, got {actual?.ToString() ?? "unknown combatant"}");
        }

        if (e.Defeated is { } defeated)
            CompareSets("defeated (at end of round)", defeated, final.Values.Where(c => c.IsDefeated).Select(c => c.Id), failures);

        foreach (var (id, text) in e.Status ?? [])
        {
            var expected = ParseStatus(text, $"expect.status.{id}");
            var actual = final.TryGetValue(id, out var c) ? c.Status.ToString().ToLowerInvariant() : "unknown combatant";
            if (actual != expected.ToString().ToLowerInvariant())
                failures.Add($"status of {id}: expected {expected.ToString().ToLowerInvariant()}, got {actual}");
        }

        foreach (var (id, rounds) in e.Bleedout ?? [])
        {
            var actual = final.TryGetValue(id, out var c) ? c.BleedoutRoundsLeft : (int?)null;
            if (actual != rounds)
                failures.Add($"bleedout rounds left for {id}: expected {rounds}, got {actual?.ToString() ?? "unknown combatant"}");
        }

        if (e.Blocked is { } blocked)
            CompareSets("blocked movers", blocked.Select(Key),
                o.Ticks.SelectMany(t => t.Movements.Where(m => m.WasBlocked).Select(m => Key(t.Index + 1, m.CombatantId))), failures);

        if (e.Hits is { } hits)
            CompareSets("hits", hits.Select(Key),
                o.Ticks.SelectMany(t => t.Attacks.Where(a => a.Hit).Select(a => Key(t.Index + 1, a.AttackerId))), failures);

        foreach (var (id, expected) in e.Cooldowns ?? [])
        {
            var actual = final.TryGetValue(id, out var c) ? c.Cooldowns : null;
            string Show(IEnumerable<KeyValuePair<string, int>> m) =>
                "{" + string.Join(", ", m.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}: {kv.Value}")) + "}";
            if (actual is null)
                failures.Add($"cooldowns of {id}: unknown combatant");
            else if (Show(actual) != Show(expected))
                failures.Add($"cooldowns of {id}: expected {Show(expected)}, got {Show(actual)}");
        }

        if (e.AbilityUses is { } uses)
            CompareSets("ability uses", uses.Select(u => Key(u.Tick, $"{u.Id}:{u.Ability}" + (u.Wasted is { } w ? $"({w})" : ""))),
                o.Ticks.SelectMany(t => t.AbilityUses.Select(u => Key(t.Index + 1,
                    $"{u.CombatantId}:{u.AbilityId}" + (u.Wasted ? $"({Snake(u.WastedReason)})" : "")))), failures);

        foreach (var want in (e.AbilityUses ?? []).Where(u => u.Targets is not null))
        {
            var use = o.Ticks.ElementAtOrDefault(want.Tick - 1)?.AbilityUses
                .FirstOrDefault(u => u.CombatantId == want.Id && u.AbilityId == want.Ability);
            if (use is not null && !use.TargetIds.SequenceEqual(want.Targets!))
                failures.Add($"targets of {want.Id}'s {want.Ability} in tick {want.Tick}: expected [{string.Join(", ", want.Targets!)}], got [{string.Join(", ", use.TargetIds)}]");
        }

        if (e.Heals is { } heals)
            CompareSets("heals", heals.Select(h => Key(h.Tick, HealKey(h.Id, h.Target, h.Amount, h.Revived, h.Wasted))),
                o.Ticks.SelectMany(t => t.Heals.Select(h => Key(t.Index + 1,
                    HealKey(h.HealerId, h.TargetId, h.Amount, h.Revived, h.Wasted ? Snake(h.WastedReason) : null)))), failures);

        if (e.Revived is { } revived)
            CompareSets("revived (back up at end of round)", revived, o.RevivedThisRound, failures);

        if (e.Wasted is { } wasted)
            CompareSets("wasted attacks", wasted.Select(Key),
                o.Ticks.SelectMany(t => t.Attacks.Where(a => a.Wasted).Select(a => Key(t.Index + 1, a.AttackerId))), failures);

        return failures;
    }

    private static string HealKey(string healer, string target, int amount, bool revived, string? wasted) =>
        $"{healer}>{target}" + (wasted is null ? $" +{amount}" : $"({wasted})") + (revived ? " revived" : "");

    private static string Key(TickRefDto r) => Key(r.Tick, r.Id ?? r.Attacker ?? "?");
    private static string Snake(WastedReason reason) =>
        Regex.Replace(reason.ToString(), "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
    private static string Key(int tick, string id) => $"{id}@tick{tick}";

    private static void CompareSets(string what, IEnumerable<string> expected, IEnumerable<string> actual, List<string> failures)
    {
        var exp = expected.Order(StringComparer.Ordinal).ToList();
        var act = actual.Order(StringComparer.Ordinal).ToList();
        if (!exp.SequenceEqual(act))
            failures.Add($"{what}: expected [{string.Join(", ", exp)}], got [{string.Join(", ", act)}]");
    }

    // ----- File format -----

    private sealed class ScenarioDto
    {
        public string? Description { get; set; }
        public int[]? Grid { get; set; }
        public List<CombatantDto>? Combatants { get; set; }
        public Dictionary<string, List<string>>? Plans { get; set; }
        public int[]? Dice { get; set; }
        public ConfigDto? Config { get; set; }
        public ExpectDto? Expect { get; set; }
        /// <summary>Ability definitions combatants can list (3B.3), same shape as abilities.json.</summary>
        public List<AbilityDto>? Abilities { get; set; }
    }

    private sealed class AbilityDto
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public int? Ap { get; set; }
        public int? Cooldown { get; set; }
        public List<string>? Tags { get; set; }
        public TargetDto? Target { get; set; }
        public List<EffectDto>? Effects { get; set; }
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
        /// <summary>Heal: die + bonus.</summary>
        public DamageDto? Amount { get; set; }
        /// <summary>Heal: brings a Downed or Stable target back up (default true).</summary>
        public bool? CanRevive { get; set; }
    }

    private sealed class DamageDto
    {
        public string? Die { get; set; }
        public int? Bonus { get; set; }
        public int? Multiplier { get; set; }
    }

    private sealed class CombatantDto
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public int? Hp { get; set; }
        public int? MaxHp { get; set; }
        public int? Speed { get; set; }
        public int? Move { get; set; }
        public int? MaxAp { get; set; }
        public int? MeleeAccuracy { get; set; }
        public int? RangedAccuracy { get; set; }
        public int? Dodge { get; set; }
        public string? Damage { get; set; }
        public int[]? At { get; set; }
        public bool? Defeated { get; set; }
        public string? Side { get; set; }
        /// <summary>Goes Downed at 0 HP instead of dying (3B.5).</summary>
        public bool? Downs { get; set; }
        /// <summary>Starting status: standing, downed, stable or dead.</summary>
        public string? Status { get; set; }
        /// <summary>Starting bleedout rounds left, when status is downed.</summary>
        public int? Bleedout { get; set; }
        /// <summary>Ability ids from the scenario's 'abilities' block (3B.3).</summary>
        public List<string>? Abilities { get; set; }
        /// <summary>Abilities already on cooldown at the start of the round: id → rounds left.</summary>
        public Dictionary<string, int>? Cooldowns { get; set; }
    }

    private sealed class ConfigDto
    {
        public int? MeleeReach { get; set; }
        public int? BleedoutRounds { get; set; }
    }

    private sealed class ExpectDto
    {
        public int? Ticks { get; set; }
        public Dictionary<string, int[]>? Positions { get; set; }
        public Dictionary<string, int>? Hp { get; set; }
        public List<string>? Defeated { get; set; }
        /// <summary>Status at the end of the round, per combatant (3B.5).</summary>
        public Dictionary<string, string>? Status { get; set; }
        /// <summary>Bleedout rounds left at the end of the round, per combatant.</summary>
        public Dictionary<string, int>? Bleedout { get; set; }
        public List<TickRefDto>? Blocked { get; set; }
        public List<TickRefDto>? Hits { get; set; }
        public List<TickRefDto>? Wasted { get; set; }
        /// <summary>AI plans: one action string, or a list of them for a multi-action queue.</summary>
        public Dictionary<string, JsonElement>? Plans { get; set; }
        /// <summary>Cooldowns at the end of the round, per combatant (exact map; {} = none) (3B.3).</summary>
        public Dictionary<string, Dictionary<string, int>>? Cooldowns { get; set; }
        /// <summary>The exact set of ability activations (3B.3).</summary>
        public List<AbilityUseDto>? AbilityUses { get; set; }
        /// <summary>The exact set of heals (3B.3).</summary>
        public List<HealDto>? Heals { get; set; }
        /// <summary>The exact set of combatants who were down at the start and are standing at the end.</summary>
        public List<string>? Revived { get; set; }
    }

    private sealed class HealDto
    {
        public int Tick { get; set; }
        /// <summary>The healer.</summary>
        public string Id { get; set; } = "";
        public string Target { get; set; } = "";
        /// <summary>HP rolled (die + bonus), before the MaxHp cap. Ignored for a wasted heal.</summary>
        public int Amount { get; set; }
        public bool Revived { get; set; }
        /// <summary>Why it was wasted ("out_of_range", "target_defeated", …), if it was.</summary>
        public string? Wasted { get; set; }
    }

    private sealed class AbilityUseDto
    {
        public int Tick { get; set; }
        public string Id { get; set; } = "";
        public string Ability { get; set; } = "";
        /// <summary>Why the activation itself was refused ("on_cooldown", "not_in_loadout"), if it was.</summary>
        public string? Wasted { get; set; }
        /// <summary>If given, the use's targets in order (for an auto ability, the ones it found).</summary>
        public List<string>? Targets { get; set; }
    }

    private sealed class TickRefDto
    {
        public int Tick { get; set; }
        public string? Id { get; set; }
        public string? Attacker { get; set; }
    }
}
