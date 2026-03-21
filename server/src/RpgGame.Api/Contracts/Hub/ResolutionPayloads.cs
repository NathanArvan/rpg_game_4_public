namespace RpgGame.Api.Contracts.Hub;

public record ResolutionStepDto(
    string Phase,
    string ActorId,
    string ActorName,
    string ActionType,
    string? TargetId,
    string? TargetName,
    int? Damage,
    string? DamageDice,
    int? NewHp,
    int? NewX,
    int? NewY,
    bool Defeated,
    string FeedText);

public record ResolutionCompletedPayload(
    string EncounterId,
    int RoundNumber,
    List<ResolutionStepDto> Steps,
    Dictionary<string, PositionDto> UpdatedPositions,
    bool EncounterEnded);
