# Session Summary: DexModifier Bug Investigation & Fix Attempt

**Date:** 2026-03-29

## Bug

`StartEncounter` failed with:

```
42703: column "DexModifier" of relation "EncounterCombatants" does not exist
```

## Root Cause

The `revert/phase-3` branch reverted PRs #54–#57. One of those PRs (`feat/custom-stats-character-rework`, PR #57) had included an EF migration that removed `DexModifier` from the `EncounterCombatants` table. That migration ran against the database before the revert.

When the PRs were reverted in code, the migration files were also removed — but EF migrations do not automatically roll back the database. The result: the code (and model snapshot) expected `DexModifier` to exist, but the database schema no longer had the column.

**Why `dotnet ef migrations add` wouldn't help:** EF generates migrations by diffing the model against the model snapshot (`AppDbContextModelSnapshot.cs`). The snapshot already showed `DexModifier` as present, so EF saw no difference and would have generated an empty migration.

## Fix Applied

A hand-written migration was created:

- `server/src/RpgGame.Infrastructure/Data/Migrations/20260328000001_RepairDexModifierColumn.cs`
- `server/src/RpgGame.Infrastructure/Data/Migrations/20260328000001_RepairDexModifierColumn.Designer.cs`

The migration uses raw SQL with `IF NOT EXISTS` so it is idempotent:

```sql
ALTER TABLE "EncounterCombatants" ADD COLUMN IF NOT EXISTS "DexModifier" integer NOT NULL DEFAULT 0;
```

This is safe on fresh environments (where `DexModifier` was never dropped) and on the affected production DB (where it is missing).

## Status

The fix was merged to `master` and deployed via CI/CD. **The error is still occurring in the remote environment.** Root cause of the deployment fix not taking effect has not yet been determined.

## Local Dev Workaround

For local dev (Docker), a full volume wipe recreates the schema correctly:

```bash
docker-compose down -v
docker-compose up --build
```
