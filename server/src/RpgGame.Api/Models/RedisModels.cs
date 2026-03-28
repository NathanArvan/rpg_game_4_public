namespace RpgGame.Api.Models;

public record PlayerEntry(string DisplayName, string CharacterId);
public record CombatantRef(string Id, string UserId, string CharacterId, string CharacterName);
public record ActiveEncounterEntry(
    string EncounterId,
    string RoundPhase,
    int RoundNumber,
    long PlanningTimerExpiresAt,
    List<CombatantRef> Combatants);
public record Position(int X, int Y);
public record MapEntry(int GridWidth, int GridHeight, Dictionary<string, Position> Positions, List<string> FogOfWar);
public record PlannedAction(string ActionType, string? TargetCombatantId, int? TargetX, int? TargetY);
