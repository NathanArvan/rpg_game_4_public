namespace RpgGame.Api.Models;

public record PlayerEntry(string DisplayName, string CharacterId);
public record CombatantRef(string Id, string UserId, string CombatantType = "player");
public record ActiveEncounterEntry(
    string EncounterId,
    string RoundPhase,
    int RoundNumber,
    DateTimeOffset PlanningTimerExpiresAt,
    List<CombatantRef> Combatants);
public record Position(int X, int Y);
public record MapEntry(int GridWidth, int GridHeight, Dictionary<string, Position> Positions, List<string> FogOfWar);
