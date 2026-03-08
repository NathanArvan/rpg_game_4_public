namespace RpgGame.Api.Contracts.Hub;

public record PositionDto(int X, int Y);

public record MapDto(
    int GridWidth,
    int GridHeight,
    Dictionary<string, PositionDto> Positions,
    List<string> FogOfWar);
