using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface ICombatService
{
    Task<AttackResult> DeclareAttackAsync(string joinCode, string userId, string targetCombatantId);
}
