using RpgGame.Rules;

namespace RpgGame.Api.Services;

/// <summary>
/// Chooses the API's dice (TESTING_ROADMAP item 6). Normally <see cref="RandomDice"/>. With
/// <c>Dice:Seed</c> set (an integer; env var <c>Dice__Seed</c>), <see cref="RoundSeededDice"/>, so
/// E2E tests can assert exact outcomes. The seed is a test setting: it's honoured only in the
/// Development and E2E environments, and anywhere else the API refuses to start rather than run a
/// real game on predictable dice.
/// </summary>
public static class DiceSetup
{
    public const string SeedKey = "Dice:Seed";

    /// <summary>Environments where <see cref="SeedKey"/> is allowed.</summary>
    public static readonly IReadOnlyList<string> SeedEnvironments = ["Development", "E2E"];

    public static IDiceRoller Create(IConfiguration config, IHostEnvironment environment)
    {
        var raw = config[SeedKey];
        if (string.IsNullOrWhiteSpace(raw))
            return new RandomDice();

        if (!SeedEnvironments.Any(environment.IsEnvironment))
            throw new InvalidOperationException(
                $"{SeedKey} is set, but seeded dice are only allowed in {string.Join(" and ", SeedEnvironments)} " +
                $"(this is {environment.EnvironmentName}). Remove the setting.");
        if (!int.TryParse(raw, out var seed))
            throw new InvalidOperationException($"{SeedKey} must be an integer, not '{raw}'.");
        return new RoundSeededDice(seed);
    }
}

/// <summary>
/// Seeded dice that start a fresh sequence every round (<see cref="ForRound"/>): a round's rolls
/// depend only on the seed and the round number, not on anything else the server rolled before
/// (other tests, other encounters). The same plan in round n of the same preset rolls the same.
/// </summary>
public sealed class RoundSeededDice(int seed) : IDiceRoller
{
    private readonly Lock _lock = new();
    private readonly SeededDice _fallback = new(seed);

    public int Seed => seed;

    /// <summary>The dice for one round's resolution.</summary>
    public IDiceRoller ForRound(int roundNumber) => new SeededDice(unchecked(seed * 1_000_003 + roundNumber));

    // Only used if something rolls outside a round's resolution: one shared sequence.
    public int RollD20() => Roll(20);

    public int Roll(int sides)
    {
        lock (_lock) return _fallback.Roll(sides);
    }
}
