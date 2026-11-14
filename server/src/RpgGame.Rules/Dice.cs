namespace RpgGame.Rules;

/// <summary>Source of dice rolls. The resolver takes one as a parameter so outcomes are reproducible.</summary>
public interface IDiceRoller
{
    int RollD20();
    int Roll(int sides);
}

/// <summary>Production dice backed by <see cref="Random.Shared"/>. Thread-safe.</summary>
public sealed class RandomDice : IDiceRoller
{
    public int RollD20() => Roll(20);
    public int Roll(int sides) => Random.Shared.Next(1, sides + 1);
}

/// <summary>Deterministic pseudo-random dice for simulations: the same seed gives the same rolls.</summary>
public sealed class SeededDice(int seed) : IDiceRoller
{
    private readonly Random _random = new(seed);

    public int RollD20() => Roll(20);
    public int Roll(int sides) => _random.Next(1, sides + 1);
}

/// <summary>
/// Returns a fixed sequence of rolls, for tests. Throws if a roll is requested after the
/// sequence is exhausted, or if a scripted value is not a valid result for the die being
/// rolled — so tests fail loudly when the number or kind of rolls changes.
/// </summary>
public sealed class ScriptedDice(params int[] rolls) : IDiceRoller
{
    private readonly int[] _rolls = (int[])rolls.Clone();
    private int _next;

    /// <summary>Rolls not yet consumed. Assert this is 0 to check every scripted roll was used.</summary>
    public int Remaining => _rolls.Length - _next;

    public int RollD20() => Roll(20);

    public int Roll(int sides)
    {
        if (_next >= _rolls.Length)
            throw new InvalidOperationException(
                $"ScriptedDice exhausted: roll #{_next + 1} (d{sides}) requested but only {_rolls.Length} scripted.");
        var value = _rolls[_next];
        if (value < 1 || value > sides)
            throw new InvalidOperationException(
                $"ScriptedDice roll #{_next + 1} is {value}, which is not a valid d{sides} result.");
        _next++;
        return value;
    }
}
