namespace RpgGame.Api.Contracts.Hub;

public record AttackResolvedPayload(
    string AttackerId,
    string AttackerName,
    string TargetId,
    string TargetName,
    int Damage,
    int TargetNewHp,
    string FeedText);
