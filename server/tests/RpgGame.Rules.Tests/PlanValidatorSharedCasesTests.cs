using System.Text.Json;
using System.Text.RegularExpressions;
using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

/// <summary>
/// The plan-validation cases shared with the client (<c>contracts/plan-validation-cases.json</c>,
/// TESTING_ROADMAP item 5): the client's <c>validatePlan</c> runs the same table, so a rule only one
/// side has fails one of the two suites. The server is the reference: messages are its own.
/// </summary>
public partial class PlanValidatorTests
{
    private static readonly Lazy<CaseFile> SharedCases = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "plan-validation-cases.json");
        return JsonSerializer.Deserialize<CaseFile>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("plan-validation-cases.json is empty");
    });

    public static IEnumerable<object[]> SharedCaseNames() => SharedCases.Value.Cases.Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(SharedCaseNames))]
    public void SharedCase(string name)
    {
        var file = SharedCases.Value;
        var c = file.Cases.Single(x => x.Name == name);
        var d = file.Defaults;

        var abilities = (c.Abilities ?? []).Select(id => file.Abilities.TryGetValue(id, out var a)
            ? ToAbility(id, a)
            : throw new InvalidDataException($"case '{name}': ability '{id}' isn't in the library")).ToList();
        var profile = CombatProfile.Default with
        {
            MaxAp = c.MaxAp ?? d.MaxAp,
            MovementRange = c.MovementRange ?? d.MovementRange,
            Abilities = abilities,
        };
        // "start": absent → the default; null → no position.
        var startCell = c.HasStart ? c.Start : d.Start;
        GridPosition? start = startCell is { Length: 2 } s ? new GridPosition(s[0], s[1]) : null;
        var positions = c.Positions?.ToDictionary(p => p.Key, p => new GridPosition(p.Value[0], p.Value[1]));
        var queue = c.Queue.Select(text => ParseAction(text, name)).ToList();

        var conditions = (c.Conditions ?? []).Select(x => new ActiveCondition(new ConditionDefinition(
            x.Name.ToLowerInvariant(), x.Name, Harmful: true, Tags: [],
            new Modifiers(Movement: x.Movement, Ap: x.Ap, Blocks: (x.Blocks ?? []).Select(b => Enum.Parse<BlockKind>(b, ignoreCase: true)).ToList())), 0)).ToList();

        var check = PlanValidator.Validate(start, profile, queue, file.Grid.Width, file.Grid.Height,
            cooldowns: c.Cooldowns, positions: positions, conditions: conditions);

        if (c.Expect.Error is { } error)
            Assert.Equal(error, check.Error);
        else
        {
            Assert.Null(check.Error);
            Assert.Equal((c.Expect.Ap, c.Expect.Cells), (check.ApSpent, check.MoveCellsUsed));
        }
    }

    [Fact]
    public void SharedCases_HaveUniqueNames_AndExpectSomething()
    {
        var cases = SharedCases.Value.Cases;
        Assert.NotEmpty(cases);
        Assert.Equal(cases.Count, cases.Select(c => c.Name).Distinct().Count());
        Assert.All(cases, c => Assert.True(c.Expect.Error is not null || c.Expect.Ap is not null, $"case '{c.Name}' expects nothing"));
    }

    private static AbilityDefinition ToAbility(string id, AbilityDto a)
    {
        var kind = a.Targeting.Kind switch
        {
            "combatant" => TargetKind.Combatant,
            "combatants" => TargetKind.Combatants,
            "self" => TargetKind.Self,
            "auto" => TargetKind.Auto,
            var k => throw new InvalidDataException($"ability {id}: unknown targeting kind '{k}'"),
        };
        List<AbilityEffect> effects = a.Move is { } m
            ? [new MoveEffect(m.To == "cell" ? MoveTo.Cell : MoveTo.Target, m.MaxCells, m.UsesMovementBudget)]
            : [new AttackEffect(AttackKind.Melee, DamageSpec.Weapon)];
        return new AbilityDefinition(id, a.Name, a.ApCost, a.Cooldown, [], new Targeting(kind, TargetFilter.Any, 1, a.Targeting.Count), effects);
    }

    /// <summary>The table's compact queue syntax (see its "about").</summary>
    private static PlannedAction ParseAction(string text, string caseName)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        GridPosition Cell(string s)
        {
            var xy = s.TrimStart('@').Split(',');
            return new GridPosition(int.Parse(xy[0]), int.Parse(xy[1]));
        }
        switch (parts[0])
        {
            case "move": return new PlannedAction(ActionType.Move, parts.Length > 1 ? Cell(parts[1]) : null);
            case "disengage": return new PlannedAction(ActionType.Disengage, parts.Length > 1 ? Cell(parts[1]) : null);
            case "melee": return PlannedAction.Melee(parts[1]);
            case "ranged": return PlannedAction.Ranged(parts[1]);
            case "stabilize": return PlannedAction.Stabilize(parts[1]);
            case "defend": return PlannedAction.Defend();
            case "ability":
                string? one = null;
                IReadOnlyList<string>? many = null;
                GridPosition? cell = null;
                foreach (var p in parts.Skip(2))
                {
                    if (p.StartsWith('@')) cell = Cell(p);
                    else if (ListRegex().Match(p) is { Success: true } list) many = list.Groups[1].Value.Split(',');
                    else one = p;
                }
                return new PlannedAction(ActionType.Ability, cell, one, parts[1], many);
            default:
                throw new InvalidDataException($"case '{caseName}': can't parse \"{text}\"");
        }
    }

    [GeneratedRegex(@"^\[(.*)\]$")]
    private static partial Regex ListRegex();

    private sealed record CaseFile(GridSize Grid, Defaults Defaults, Dictionary<string, AbilityDto> Abilities, List<Case> Cases);
    private sealed record GridSize(int Width, int Height);
    private sealed record Defaults(int MaxAp, int MovementRange, int[] Start);
    private sealed record AbilityDto(string Name, int ApCost, int Cooldown, TargetingDto Targeting, MoveDto? Move);
    private sealed record TargetingDto(string Kind, int? Count);
    private sealed record MoveDto(string To, int MaxCells, bool UsesMovementBudget);
    private sealed record ConditionCase(string Name, int Movement, int Ap, List<string>? Blocks);
    private sealed record Expectation(string? Error, int? Ap, int? Cells);

    private sealed class Case
    {
        public string Name { get; init; } = "";
        public int? MaxAp { get; init; }
        public int? MovementRange { get; init; }
        public List<string>? Abilities { get; init; }
        public Dictionary<string, int>? Cooldowns { get; init; }
        public Dictionary<string, int[]>? Positions { get; init; }
        public List<ConditionCase>? Conditions { get; init; }
        public List<string> Queue { get; init; } = [];
        public Expectation Expect { get; init; } = new(null, null, null);

        private int[]? _start;
        public int[]? Start { get => _start; init { _start = value; HasStart = true; } }
        public bool HasStart { get; private init; }
    }
}
