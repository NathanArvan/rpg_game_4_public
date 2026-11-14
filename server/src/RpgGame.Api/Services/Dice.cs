namespace RpgGame.Api.Services;

/// <summary>Source of dice rolls. Injected so tests can script exact outcomes.</summary>
public interface IDiceRoller
{
    int RollD20();
    int Roll(int sides);
}

/// <summary>Production dice backed by <see cref="Random.Shared"/>.</summary>
public sealed class RandomDice : IDiceRoller
{
    public int RollD20() => Roll(20);
    public int Roll(int sides) => Random.Shared.Next(1, sides + 1);
}

/// <summary>
/// Returns a fixed sequence of rolls. Throws if a roll is requested after the
/// sequence is exhausted, or if a scripted value is outside 1..sides.
/// </summary>
public sealed class ScriptedDice(params int[] rolls) : IDiceRoller
{
    private int _next;

    public int Remaining => rolls.Length - _next;

    public int RollD20() => Roll(20);

    public int Roll(int sides)
    {
        if (_next >= rolls.Length)
            throw new InvalidOperationException(
                $"ScriptedDice exhausted: roll #{_next + 1} (d{sides}) requested but only {rolls.Length} scripted.");
        var value = rolls[_next];
        if (value < 1 || value > sides)
            throw new InvalidOperationException(
                $"ScriptedDice roll #{_next + 1} is {value}, which is not a valid d{sides} result.");
        _next++;
        return value;
    }
}
