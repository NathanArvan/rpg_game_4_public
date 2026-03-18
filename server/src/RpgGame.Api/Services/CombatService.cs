using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using System.Text.Json;

namespace RpgGame.Api.Services;

public class CombatService(AppDbContext db, IRedisGameState redisState) : ICombatService
{
    public async Task<AttackResult> DeclareAttackAsync(string joinCode, string userId, string targetCombatantId)
    {
        var gameSession = await db.GameSessions.FirstOrDefaultAsync(s => s.JoinCode == joinCode)
            ?? throw new GameActionException("Invalid join code.", isActionRejected: true);

        var sessionId = gameSession.Id.ToString();

        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionId)
            ?? throw new GameActionException("No active encounter.", isActionRejected: true);

        var activeCombatantRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == activeEncounter.ActiveTurnCombatantId);
        if (activeCombatantRef is null || activeCombatantRef.UserId != userId)
            throw new GameActionException("It is not your turn.", isActionRejected: true);

        if (activeCombatantRef.Id == targetCombatantId)
            throw new GameActionException("You cannot attack yourself.", isActionRejected: true);

        var targetRef = activeEncounter.Combatants.FirstOrDefault(c => c.Id == targetCombatantId)
            ?? throw new GameActionException("Target not found.", isActionRejected: true);

        if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, activeCombatantRef.Id))
            throw new GameActionException("You are defeated.", isActionRejected: true);

        if (await redisState.IsDefeatedAsync(activeEncounter.EncounterId, targetCombatantId))
            throw new GameActionException("Target is already defeated.", isActionRejected: true);

        if (!Guid.TryParse(activeEncounter.EncounterId, out var encounterGuid))
            throw new GameActionException("Invalid encounter ID.", isActionRejected: true);

        if (!Guid.TryParse(targetCombatantId, out var targetCombatantGuid))
            throw new GameActionException("Invalid target ID.", isActionRejected: true);

        var attackerDbCombatant = await db.EncounterCombatants
            .FirstOrDefaultAsync(c => c.EncounterId == encounterGuid && c.UserId == userId);
        var attackerChar = attackerDbCombatant is not null
            ? await db.Characters.FirstOrDefaultAsync(c => c.Id == attackerDbCombatant.CharacterId)
            : null;
        var attackerClass = attackerChar?.Class ?? "";
        var attackerName = attackerChar?.Name ?? "Unknown";

        var targetDbCombatant = await db.EncounterCombatants.FirstOrDefaultAsync(c => c.Id == targetCombatantGuid)
            ?? throw new GameActionException("Target combatant not found in DB.", isActionRejected: true);

        var targetChar = await db.Characters.FirstOrDefaultAsync(c => c.Id == targetDbCombatant.CharacterId);
        var targetName = targetChar?.Name ?? "Unknown";

        var damage = CombatDice.RollDamage(attackerClass);
        var diceString = CombatDice.DamageDiceString(attackerClass);

        var newHpRaw = await redisState.DecrementHpAsync(targetDbCombatant.CharacterId.ToString(), damage);
        var newHp = (int)Math.Max(0, newHpRaw);
        if (newHpRaw < 0)
            await redisState.SetHpAsync(targetDbCombatant.CharacterId.ToString(), 0);

        var isDefeated = newHp <= 0;

        var eventPayload = JsonSerializer.Serialize(new
        {
            attackerId = activeCombatantRef.Id,
            attackerName,
            targetId = targetCombatantId,
            targetName,
            damageDice = diceString,
            damage,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        db.GameEvents.Add(new GameEvent
        {
            Id = Guid.NewGuid(),
            EncounterId = encounterGuid,
            OccurredAt = DateTimeOffset.UtcNow,
            EventType = "attack",
            Payload = eventPayload,
        });
        await db.SaveChangesAsync();

        if (isDefeated)
            await redisState.MarkDefeatedAsync(activeEncounter.EncounterId, targetCombatantId);

        var feedText = isDefeated
            ? $"{attackerName} attacks {targetName} for {damage} damage! {targetName} is defeated!"
            : $"{attackerName} attacks {targetName} for {damage} damage! ({newHp} HP remaining)";

        var attackPayload = new AttackResolvedPayload(
            activeCombatantRef.Id,
            attackerName,
            targetCombatantId,
            targetName,
            damage,
            newHp,
            feedText);

        var defeatedPayload = isDefeated ? new CombatantDefeatedPayload(targetCombatantId) : null;

        return new AttackResult(gameSession.Id, attackPayload, defeatedPayload);
    }
}
