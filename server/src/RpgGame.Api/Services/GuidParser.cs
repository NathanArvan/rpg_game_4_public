namespace RpgGame.Api.Services;

internal static class GuidParser
{
    public static Guid ParseOrThrow(string? value, string paramName)
    {
        if (!Guid.TryParse(value, out var guid))
            throw new GameActionException($"Invalid {paramName}.", isActionRejected: true);
        return guid;
    }

    public static Guid? TryParse(string? value) =>
        Guid.TryParse(value, out var guid) ? guid : null;
}
