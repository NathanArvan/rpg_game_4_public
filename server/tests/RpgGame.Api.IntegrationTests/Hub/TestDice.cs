using RpgGame.Rules;

namespace RpgGame.Api.IntegrationTests.Hub;

/// <summary>
/// The API's dice in hub tests: like <see cref="ScriptedDice"/> (fails loudly when a roll isn't
/// scripted or doesn't fit the die), but one instance lives in the server for the whole test run
/// and each test scripts its own rolls with <see cref="Script"/>.
/// </summary>
public sealed class TestDice : IDiceRoller
{
    private readonly Lock _lock = new();
    private ScriptedDice _current = new();

    /// <summary>Replaces whatever is left with these rolls, in the order the server will ask for them.</summary>
    public void Script(params int[] rolls)
    {
        lock (_lock) _current = new ScriptedDice(rolls);
    }

    /// <summary>Scripted rolls not yet used; 0 once the server rolled exactly what the test expected.</summary>
    public int Remaining
    {
        get { lock (_lock) return _current.Remaining; }
    }

    public int RollD20() => Roll(20);

    public int Roll(int sides)
    {
        lock (_lock) return _current.Roll(sides);
    }
}
