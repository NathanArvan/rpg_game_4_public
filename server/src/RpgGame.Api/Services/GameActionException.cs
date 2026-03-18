namespace RpgGame.Api.Services;

public class GameActionException(string message, bool isActionRejected = false) : Exception(message)
{
    public bool IsActionRejected { get; } = isActionRejected;
}
