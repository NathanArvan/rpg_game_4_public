using Microsoft.EntityFrameworkCore;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using System.Text.Json;

namespace RpgGame.Api.Services;

public class ResolutionService(AppDbContext db, IRedisGameState redisState) : IResolutionService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Internal state snapshot per combatant for a resolution run
    private record CombatantSnapshot(
        string CombatantId,
        string CharacterId,
        string CharacterName,
        int CurrentHp,
        int MaxHp,
        string CharClass,
        int Dex,
        bool IsDefeated,
        Position CurrentPosition);

    // Tracks a combatant's movement through the lockstep loop
    private class MoverState
    {
        public string CombatantId { get; init; } = "";
        public List<Position> FullPath { get; init; } = [];
        public int Dex { get; init; }
        public int CurrentStep { get; set; } = 0;
        public bool Stopped { get; set; } = false;
        public string? BlockedBy { get; set; }
    }

    // Intermediate attack result before HP deltas are applied
    private class AttackComputation
    {
        public string AttackerId { get; init; } = "";
        public string TargetId { get; init; } = "";
        public string AttackType { get; init; } = "";
        public bool Wasted { get; set; }
        public string WastedReason { get; set; } = "";
        public bool Hit { get; set; }
        public int Damage { get; set; }
    }

    public async Task<ResolutionPayload> ResolveRoundAsync(Guid encounterId)
    {
        // Load encounter with combatants from DB
        var encounter = await db.Encounters
            .Include(e => e.Combatants)
            .FirstOrDefaultAsync(e => e.Id == encounterId)
            ?? throw new InvalidOperationException($"Encounter {encounterId} not found.");

        var sessionIdStr = encounter.GameSessionId.ToString();
        var encounterIdStr = encounterId.ToString();

        // Load character stats from DB (class, dex, maxHp)
        var charIds = encounter.Combatants.Select(c => c.CharacterId).ToList();
        var characterMap = await db.Characters
            .Where(c => charIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        // Load planned actions from Redis
        var plannedActionsJson = await redisState.GetAllPlannedActionsAsync(encounterIdStr);
        var plannedActions = new Dictionary<string, PlannedActionArray>();
        foreach (var (combatantId, actionJson) in plannedActionsJson)
        {
            var action = JsonSerializer.Deserialize<PlannedActionArray>(actionJson, _jsonOptions);
            if (action is not null)
                plannedActions[combatantId] = action;
        }

        // Load map from Redis
        var mapEntry = await redisState.GetMapAsync(encounterIdStr)
            ?? throw new InvalidOperationException($"Map for encounter {encounterId} not found.");

        // Build per-combatant snapshots
        var snapshots = new Dictionary<string, CombatantSnapshot>();
        foreach (var c in encounter.Combatants)
        {
            var combatantId = c.Id.ToString();
            var currentHp = await redisState.GetCharacterHpAsync(c.CharacterId.ToString());
            var isDefeated = await redisState.IsDefeatedAsync(encounterIdStr, combatantId);
            var pos = mapEntry.Positions.GetValueOrDefault(combatantId, new Position(0, 0));
            characterMap.TryGetValue(c.CharacterId, out var character);

            snapshots[combatantId] = new CombatantSnapshot(
                CombatantId: combatantId,
                CharacterId: c.CharacterId.ToString(),
                CharacterName: c.CharacterName,
                CurrentHp: currentHp,
                MaxHp: character?.MaxHp ?? currentHp,
                CharClass: character?.Class ?? "wizard",
                Dex: character?.BaseStats.Dex ?? 10,
                IsDefeated: isDefeated,
                CurrentPosition: pos);
        }

        // Working copies (mutated during resolution)
        var workingPositions = new Dictionary<string, Position>(mapEntry.Positions);
        var workingHp = snapshots.ToDictionary(kv => kv.Key, kv => kv.Value.CurrentHp);
        var alreadyDefeated = new HashSet<string>(snapshots.Where(kv => kv.Value.IsDefeated).Select(kv => kv.Key));
        var defeatedThisRound = new HashSet<string>();

        // Tick loop — always 1 tick in 3A (extension point for future AP system)
        var tickResults = new List<TickResult>();
        const int maxTicks = 1;

        for (int tick = 0; tick < maxTicks; tick++)
        {
            var movements = ResolveMovementPhase(plannedActions, snapshots, workingPositions, alreadyDefeated, defeatedThisRound);
            var attacks = ResolveAttackPhase(plannedActions, snapshots, workingPositions, workingHp, alreadyDefeated, defeatedThisRound);
            var effects = ResolveEffectsPhase(plannedActions, snapshots, alreadyDefeated, defeatedThisRound);
            tickResults.Add(new TickResult(tick, movements, attacks, effects));
        }

        // Build updated combatant list for next planning phase
        var activeEncounter = await redisState.GetActiveEncounterAsync(sessionIdStr);
        var newRoundNumber = (activeEncounter?.RoundNumber ?? 1) + 1;
        var allDefeatedIds = new HashSet<string>(alreadyDefeated.Union(defeatedThisRound));

        var updatedCombatants = encounter.Combatants.Select(c =>
        {
            var id = c.Id.ToString();
            var snap = snapshots[id];
            return new PlanningCombatantDto(
                id,
                c.UserId,
                c.CharacterName,
                workingHp.GetValueOrDefault(id, snap.CurrentHp),
                snap.MaxHp,
                allDefeatedIds.Contains(id));
        }).ToList();

        // Write GameEvents to DB BEFORE Redis mutations
        var events = BuildGameEvents(encounterId, tickResults);
        if (events.Count > 0)
        {
            db.GameEvents.AddRange(events);
            await db.SaveChangesAsync();
        }

        // Write Redis mutations
        foreach (var (combatantId, newHp) in workingHp)
        {
            var snap = snapshots[combatantId];
            if (newHp != snap.CurrentHp)
                await redisState.SetHpAsync(snap.CharacterId, newHp);
        }

        foreach (var combatantId in defeatedThisRound)
            await redisState.MarkDefeatedAsync(encounterIdStr, combatantId);

        await redisState.SetMapAsync(encounterIdStr, new MapEntry(
            mapEntry.GridWidth, mapEntry.GridHeight,
            workingPositions, mapEntry.FogOfWar));

        await redisState.ClearPlannedActionsAsync(encounterIdStr);
        await redisState.ClearLockedInAsync(encounterIdStr);

        // Transition to between_rounds — Hub will transition to planning after the broadcast delay
        if (activeEncounter is not null)
        {
            var betweenRounds = activeEncounter with { RoundPhase = "between_rounds", RoundNumber = newRoundNumber };
            await redisState.SetActiveEncounterAsync(sessionIdStr, betweenRounds);
        }

        return new ResolutionPayload(tickResults, newRoundNumber, defeatedThisRound.ToList(), updatedCombatants);
    }

    // ----- Phase 1: Movement -----

    private static List<MovementResult> ResolveMovementPhase(
        Dictionary<string, PlannedActionArray> plannedActions,
        Dictionary<string, CombatantSnapshot> snapshots,
        Dictionary<string, Position> workingPositions,
        HashSet<string> alreadyDefeated,
        HashSet<string> defeatedThisRound)
    {
        var results = new List<MovementResult>();

        // Build mover list
        var movers = new List<MoverState>();
        foreach (var (combatantId, actionArray) in plannedActions)
        {
            if (alreadyDefeated.Contains(combatantId) || defeatedThisRound.Contains(combatantId)) continue;
            var action = actionArray.Actions.FirstOrDefault();
            if (action?.Type != "move" || action.TargetCell is null) continue;

            var startPos = workingPositions.GetValueOrDefault(combatantId, new Position(0, 0));
            var targetPos = new Position(action.TargetCell.X, action.TargetCell.Y);
            var path = ComputeChebyshevPath(startPos, targetPos);

            if (path.Count <= 1) continue; // Already at target

            snapshots.TryGetValue(combatantId, out var snap);
            movers.Add(new MoverState
            {
                CombatantId = combatantId,
                FullPath = path,
                Dex = snap?.Dex ?? 10,
            });
        }

        if (movers.Count == 0) return results;

        int maxSteps = movers.Max(m => m.FullPath.Count - 1);

        for (int step = 0; step < maxSteps; step++)
        {
            var active = movers.Where(m => !m.Stopped && m.CurrentStep < m.FullPath.Count - 1).ToList();
            if (active.Count == 0) break;

            // Desired next positions
            var desired = active.ToDictionary(m => m.CombatantId, m => m.FullPath[m.CurrentStep + 1]);

            var blocked = new HashSet<string>();

            // Resolve mover-vs-mover conflicts (two movers claiming same cell)
            var cellClaims = desired
                .GroupBy(kv => kv.Value, new PositionComparer())
                .Where(g => g.Count() > 1);

            foreach (var group in cellClaims)
            {
                var claimants = group.Select(kv => kv.Key).ToList();
                var sorted = claimants.OrderByDescending(id =>
                    movers.First(m => m.CombatantId == id).Dex).ToList();

                int highDex = movers.First(m => m.CombatantId == sorted[0]).Dex;
                int secondDex = movers.First(m => m.CombatantId == sorted[1]).Dex;

                if (highDex == secondDex)
                {
                    // Tied Dex — all claimants stop
                    foreach (var id in claimants)
                    {
                        blocked.Add(id);
                        var mover = movers.First(m => m.CombatantId == id);
                        mover.BlockedBy = claimants.First(other => other != id);
                    }
                }
                else
                {
                    // Winner is sorted[0], losers stop
                    foreach (var id in claimants.Skip(1))
                    {
                        blocked.Add(id);
                        var mover = movers.First(m => m.CombatantId == id);
                        mover.BlockedBy = sorted[0];
                    }
                }
            }

            // Resolve mover-vs-stationary conflicts (mover wants cell occupied by a non-active-mover)
            foreach (var (moverId, nextCell) in desired)
            {
                if (blocked.Contains(moverId)) continue;

                foreach (var (occupantId, pos) in workingPositions)
                {
                    if (occupantId == moverId) continue;
                    if (pos.X != nextCell.X || pos.Y != nextCell.Y) continue;

                    // Is this occupant a currently active mover who is not blocked?
                    bool isActiveUnblockedMover = active.Any(m => m.CombatantId == occupantId)
                        && !blocked.Contains(occupantId);

                    if (!isActiveUnblockedMover)
                    {
                        blocked.Add(moverId);
                        movers.First(m => m.CombatantId == moverId).BlockedBy = occupantId;
                        break;
                    }
                }
            }

            // Apply step
            foreach (var mover in active)
            {
                if (blocked.Contains(mover.CombatantId))
                {
                    mover.Stopped = true;
                }
                else
                {
                    mover.CurrentStep++;
                    workingPositions[mover.CombatantId] = mover.FullPath[mover.CurrentStep];
                }
            }
        }

        // Build MovementResults
        foreach (var mover in movers)
        {
            var takenPath = mover.FullPath
                .Take(mover.CurrentStep + 1)
                .Select(p => new PositionDto(p.X, p.Y))
                .ToList();
            var finalPos = mover.FullPath[mover.CurrentStep];
            snapshots.TryGetValue(mover.CombatantId, out var snap);

            results.Add(new MovementResult(
                mover.CombatantId,
                snap?.CharacterName ?? "Unknown",
                takenPath,
                new PositionDto(finalPos.X, finalPos.Y),
                mover.Stopped,
                mover.BlockedBy));
        }

        return results;
    }

    // ----- Phase 2: Attacks -----

    private static List<AttackResult> ResolveAttackPhase(
        Dictionary<string, PlannedActionArray> plannedActions,
        Dictionary<string, CombatantSnapshot> snapshots,
        Dictionary<string, Position> workingPositions,
        Dictionary<string, int> workingHp,
        HashSet<string> alreadyDefeated,
        HashSet<string> defeatedThisRound)
    {
        var results = new List<AttackResult>();
        var computations = new List<AttackComputation>();

        // Collect attack actions and compute hit/damage
        var hpDeltas = new Dictionary<string, int>(); // targetId → net delta (negative = damage)

        foreach (var (combatantId, actionArray) in plannedActions)
        {
            if (alreadyDefeated.Contains(combatantId) || defeatedThisRound.Contains(combatantId)) continue;
            var action = actionArray.Actions.FirstOrDefault();
            if (action?.Type is not ("melee_attack" or "ranged_attack")) continue;

            var targetId = action.TargetCombatantId ?? "";
            var comp = new AttackComputation
            {
                AttackerId = combatantId,
                TargetId = targetId,
                AttackType = action.Type,
            };

            // Validate attack
            if (!snapshots.ContainsKey(targetId) || alreadyDefeated.Contains(targetId) || defeatedThisRound.Contains(targetId))
            {
                comp.Wasted = true;
                comp.WastedReason = "target already defeated";
            }
            else if (action.Type == "melee_attack")
            {
                var attackerPos = workingPositions.GetValueOrDefault(combatantId, new Position(0, 0));
                var targetPos = workingPositions.GetValueOrDefault(targetId, new Position(0, 0));
                var chebyshev = Math.Max(Math.Abs(attackerPos.X - targetPos.X), Math.Abs(attackerPos.Y - targetPos.Y));
                if (chebyshev > 1)
                {
                    comp.Wasted = true;
                    comp.WastedReason = "target out of melee range";
                }
            }

            if (!comp.Wasted)
            {
                snapshots.TryGetValue(combatantId, out var attacker);
                int hitRoll = CombatDice.RollD20(); // +0 accuracyBonus in 3A
                comp.Hit = hitRoll >= 10;            // dodgeThreshold = 10 in 3A

                if (comp.Hit)
                {
                    comp.Damage = CombatDice.RollDamage(attacker?.CharClass ?? "wizard");
                    hpDeltas[targetId] = hpDeltas.GetValueOrDefault(targetId, 0) - comp.Damage;
                }
            }

            computations.Add(comp);
        }

        // Snapshot HP before applying deltas (for feed text "HP: X → Y")
        var hpBefore = new Dictionary<string, int>(workingHp);

        // Apply all HP deltas in one batch
        foreach (var (targetId, delta) in hpDeltas)
            workingHp[targetId] = workingHp.GetValueOrDefault(targetId, 0) + delta;

        // Check defeats AFTER all damage applied
        foreach (var (targetId, newHp) in workingHp)
        {
            if (!alreadyDefeated.Contains(targetId) && !defeatedThisRound.Contains(targetId) && newHp <= 0)
                defeatedThisRound.Add(targetId);
        }

        // Build AttackResults
        foreach (var comp in computations)
        {
            snapshots.TryGetValue(comp.AttackerId, out var attacker);
            snapshots.TryGetValue(comp.TargetId, out var target);
            int newHp = workingHp.GetValueOrDefault(comp.TargetId, 0);
            bool targetDefeated = defeatedThisRound.Contains(comp.TargetId);
            string attackerName = attacker?.CharacterName ?? "Unknown";
            string targetName = target?.CharacterName ?? "Unknown";
            string attackVerb = comp.AttackType == "ranged_attack" ? "shoots at" : "strikes";

            string feedText;
            if (comp.Wasted)
                feedText = $"{attackerName}'s attack on {targetName} fails — {comp.WastedReason}";
            else if (!comp.Hit)
                feedText = $"{attackerName} swings at {targetName} and misses";
            else if (targetDefeated)
                feedText = $"{attackerName} {attackVerb} {targetName} for {comp.Damage} damage — defeated!";
            else
                feedText = $"{attackerName} {attackVerb} {targetName} for {comp.Damage} damage (HP: {hpBefore.GetValueOrDefault(comp.TargetId, 0)} → {newHp})";

            results.Add(new AttackResult(
                comp.AttackerId, attackerName,
                comp.TargetId, targetName,
                comp.AttackType, comp.Hit, comp.Damage,
                newHp, targetDefeated, comp.Wasted, feedText));
        }

        return results;
    }

    // ----- Phase 3: Effects -----

    private static List<EffectResult> ResolveEffectsPhase(
        Dictionary<string, PlannedActionArray> plannedActions,
        Dictionary<string, CombatantSnapshot> snapshots,
        HashSet<string> alreadyDefeated,
        HashSet<string> defeatedThisRound)
    {
        var results = new List<EffectResult>();
        foreach (var (combatantId, actionArray) in plannedActions)
        {
            if (alreadyDefeated.Contains(combatantId) || defeatedThisRound.Contains(combatantId)) continue;
            var action = actionArray.Actions.FirstOrDefault();
            if (action?.Type != "defend") continue;
            snapshots.TryGetValue(combatantId, out var snap);
            results.Add(new EffectResult(combatantId, "defend", $"{snap?.CharacterName ?? "Unknown"} holds position"));
        }
        return results;
    }

    // ----- Helpers -----

    private static List<Position> ComputeChebyshevPath(Position start, Position target)
    {
        var path = new List<Position> { start };
        var current = start;
        while (current.X != target.X || current.Y != target.Y)
        {
            int dx = target.X - current.X;
            int dy = target.Y - current.Y;
            int stepX = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
            int stepY = dy == 0 ? 0 : (dy > 0 ? 1 : -1);
            current = new Position(current.X + stepX, current.Y + stepY);
            path.Add(current);
        }
        return path;
    }

    private static List<GameEvent> BuildGameEvents(Guid encounterId, List<TickResult> tickResults)
    {
        var events = new List<GameEvent>();
        var now = DateTimeOffset.UtcNow;

        foreach (var tick in tickResults)
        {
            foreach (var movement in tick.Movements)
            {
                events.Add(new GameEvent
                {
                    Id = Guid.NewGuid(),
                    EncounterId = encounterId,
                    OccurredAt = now,
                    EventType = "movement",
                    Payload = JsonSerializer.Serialize(new { movement.CombatantId, movement.FinalPosition, movement.WasBlocked }, _jsonOptions),
                });
            }
            foreach (var attack in tick.Attacks)
            {
                var eventType = attack.Wasted ? "attack_wasted" : (attack.Hit ? "attack_hit" : "attack_miss");
                events.Add(new GameEvent
                {
                    Id = Guid.NewGuid(),
                    EncounterId = encounterId,
                    OccurredAt = now,
                    EventType = eventType,
                    Payload = JsonSerializer.Serialize(new { attack.AttackerId, attack.TargetId, attack.Damage, attack.TargetNewHp }, _jsonOptions),
                });
            }
        }

        return events;
    }

    // Equality comparer for Position used in LINQ GroupBy
    private class PositionComparer : IEqualityComparer<Position>
    {
        public bool Equals(Position? x, Position? y) =>
            x is not null && y is not null && x.X == y.X && x.Y == y.Y;

        public int GetHashCode(Position p) => HashCode.Combine(p.X, p.Y);
    }
}
