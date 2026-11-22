using System.Text.Json;
using System.Text.RegularExpressions;
using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.IntegrationTests.Hub;

/// <summary>
/// Checks JSON from the wire against the client's TypeScript contracts
/// (<c>client/src/app/contracts/hub-contracts.generated.ts</c>): every field the server sends is
/// declared by the client, every field the client declares is sent, and null only arrives where
/// the client allows it — recursively through nested contracts, arrays and maps.
/// </summary>
public static partial class ContractShape
{
    /// <summary>Which client interface each broadcast's payload is.</summary>
    public static readonly IReadOnlyDictionary<string, string> PayloadInterface = new Dictionary<string, string>
    {
        [HubEvents.Events.PlayerPresenceChanged] = "PlayerPresenceChangedPayload",
        [HubEvents.Events.EncounterStarted] = "EncounterStartedPayload",
        [HubEvents.Events.PlanningPhaseStarted] = "PlanningPhaseStartedPayload",
        [HubEvents.Events.PlayerLockedIn] = "PlayerLockedInPayload",
        [HubEvents.Events.AllLockedIn] = "AllLockedInPayload",
        [HubEvents.Events.ActionRejected] = "ActionRejectedPayload",
        [HubEvents.Events.CombatantDefeated] = "CombatantDefeatedPayload",
        [HubEvents.Events.EncounterEnded] = "EncounterEndedPayload",
        [HubEvents.Events.ResolutionCompleted] = "ResolutionPayload",
        [HubEvents.Events.MonsterAdded] = "MonsterAddedPayload",
    };

    public sealed record TsField(string Name, bool Optional, string Type);

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<TsField>>> _shipped =
        new(() => Parse(File.ReadAllText(FindGeneratedContracts())));

    /// <summary>The interfaces in the committed generated contracts file.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TsField>> Shipped => _shipped.Value;

    /// <summary>Every received event's payload matches its client interface.</summary>
    public static void AssertEventsMatch(IEnumerable<ReceivedEvent> events)
    {
        var problems = events
            .SelectMany(e => PayloadInterface.TryGetValue(e.Name, out var iface)
                ? Check(e.Payload, iface, Shipped).Select(p => $"{e.Name}: {p}")
                : [$"{e.Name}: no client interface mapped (add it to ContractShape.PayloadInterface)"])
            .Distinct()
            .ToList();
        Assert.True(problems.Count == 0, "Wire JSON doesn't match the client contracts:\n" + string.Join("\n", problems));
    }

    /// <summary>The problems found checking <paramref name="json"/> against interface <paramref name="iface"/>; empty when it matches.</summary>
    public static IReadOnlyList<string> Check(JsonElement json, string iface, IReadOnlyDictionary<string, IReadOnlyList<TsField>> interfaces)
    {
        var problems = new List<string>();
        CheckObject(json, iface, iface, interfaces, problems);
        return problems;
    }

    private static void CheckObject(JsonElement json, string iface, string path,
        IReadOnlyDictionary<string, IReadOnlyList<TsField>> interfaces, List<string> problems)
    {
        if (!interfaces.TryGetValue(iface, out var fields))
        {
            problems.Add($"{path}: the client has no interface {iface}");
            return;
        }
        if (json.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}: expected an object ({iface}), got {json.ValueKind}");
            return;
        }

        var declared = fields.ToDictionary(f => f.Name);
        foreach (var prop in json.EnumerateObject())
        {
            if (!declared.TryGetValue(prop.Name, out var field))
                problems.Add($"{path}.{prop.Name}: sent by the server but not declared by the client's {iface}");
            else
                CheckValue(prop.Value, field.Type, $"{path}.{prop.Name}", interfaces, problems);
        }
        foreach (var field in fields.Where(f => !f.Optional && !json.TryGetProperty(f.Name, out _)))
            problems.Add($"{path}.{field.Name}: declared by the client's {iface} but not sent by the server");
    }

    private static void CheckValue(JsonElement value, string type, string path,
        IReadOnlyDictionary<string, IReadOnlyList<TsField>> interfaces, List<string> problems)
    {
        type = type.Trim();
        var nullable = type.EndsWith("| null");
        if (nullable) type = type[..^"| null".Length].Trim();

        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!nullable) problems.Add($"{path}: null, but the client's type is {type}");
            return;
        }

        var map = MapType().Match(type);
        if (map.Success)
        {
            if (value.ValueKind != JsonValueKind.Object) { problems.Add($"{path}: expected a map, got {value.ValueKind}"); return; }
            foreach (var entry in value.EnumerateObject())
                CheckValue(entry.Value, map.Groups[1].Value, $"{path}[{entry.Name}]", interfaces, problems);
            return;
        }
        if (type.EndsWith("[]"))
        {
            if (value.ValueKind != JsonValueKind.Array) { problems.Add($"{path}: expected an array, got {value.ValueKind}"); return; }
            var i = 0;
            foreach (var item in value.EnumerateArray())
                CheckValue(item, type[..^2], $"{path}[{i++}]", interfaces, problems);
            return;
        }

        var ok = type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "unknown" or "any" => true,
            _ => true, // an interface: checked below
        };
        if (!ok) { problems.Add($"{path}: expected {type}, got {value.ValueKind}"); return; }
        if (interfaces.ContainsKey(type))
            CheckObject(value, type, path, interfaces, problems);
        else if (type is not ("string" or "number" or "boolean" or "unknown" or "any"))
            problems.Add($"{path}: the client type {type} isn't an interface in the contracts file");
    }

    /// <summary>Reads the interfaces (name → fields) out of a generated contracts file.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TsField>> Parse(string ts)
    {
        var result = new Dictionary<string, IReadOnlyList<TsField>>();
        string? current = null;
        List<TsField> fields = [];
        foreach (var raw in ts.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var start = InterfaceStart().Match(line);
            if (start.Success)
            {
                current = start.Groups[1].Value;
                fields = [];
                if (line.TrimEnd().EndsWith("{}")) { result[current] = fields; current = null; }
                continue;
            }
            if (current is null) continue;
            if (line.Trim() == "}")
            {
                result[current] = fields;
                current = null;
                continue;
            }
            var field = FieldLine().Match(line);
            if (field.Success)
                fields.Add(new TsField(field.Groups[1].Value, field.Groups[2].Value == "?", field.Groups[3].Value));
        }
        return result;
    }

    private static string FindGeneratedContracts()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "client", "src", "app", "contracts", "hub-contracts.generated.ts");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("client/src/app/contracts/hub-contracts.generated.ts not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^export interface (\w+)\s*\{")]
    private static partial Regex InterfaceStart();

    [GeneratedRegex(@"^\s+(\w+)(\??):\s*(.+);\s*$")]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"^\{\s*\[key: string\]:\s*(.+?)\s*\}$")]
    private static partial Regex MapType();
}
