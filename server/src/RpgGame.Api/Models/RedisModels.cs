namespace RpgGame.Api.Models;

public record PlayerEntry(string DisplayName, string CharacterId);
public record CombatantRef(string Id, string UserId, string CombatantType = "player", bool IsAiControlled = false);

/// <summary>A monster staged in the lobby before the encounter starts.</summary>
/// <param name="Profile">Combat profile id; null (entries from before 3B.1) means the default enemy profile.</param>
public record PendingMonster(string Name, bool IsAiControlled, string? Profile = null);
public record ActiveEncounterEntry(
    string EncounterId,
    string RoundPhase,
    int RoundNumber,
    DateTimeOffset PlanningTimerExpiresAt,
    List<CombatantRef> Combatants,
    bool PlanningTimerEnabled = true);
public record Position(int X, int Y);
public record MapEntry(int GridWidth, int GridHeight, Dictionary<string, Position> Positions, List<string> FogOfWar);
