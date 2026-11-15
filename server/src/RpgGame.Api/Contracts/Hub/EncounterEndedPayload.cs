namespace RpgGame.Api.Contracts.Hub;

/// <param name="WinningSide">"party", "enemy", or "draw" (both sides defeated in the same round).</param>
/// <param name="FinalRoundNumber">The last round that was resolved.</param>
public record EncounterEndedPayload(Guid EncounterId, string WinningSide, int FinalRoundNumber);
