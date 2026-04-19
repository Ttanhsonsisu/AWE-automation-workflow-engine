# Release Notes – Cron/Quartz Stabilization & Scheduler Reconciliation

## Summary
This release stabilizes cron workflow execution and scheduler synchronization across multi-host deployments.

### Key outcomes
- Fixed cron workflows that only logged publish events without creating/running workflow instances.
- Added backward-compatible cron parsing semantics using Cronos (5-field + supported 6-field handling).
- Migrated runtime scheduling behavior to Quartz one-shot triggers with self-reschedule.
- Added robust scheduler reconciliation flow for Publish/Unpublish/Delete lifecycle.
- Reduced multi-host race conditions with distributed locking.

## Main fixes included

### 1) Cron trigger execution correctness
- Support for trigger type variants (`CronTrigger` + `CronTriggerPlugin`, etc.).
- Cron events can target specific trigger step IDs instead of starting all cron trigger nodes.

### 2) Messaging path reliability
- Cron job publish now uses explicit routing key (`workflow.job.submit`).
- Consumer replies are guarded to request/response context only, avoiding publish-path faults.

### 3) Multi-cron definitions
- Schedule extraction now supports multiple cron trigger nodes within one definition.
- Quartz identity is scoped to `{DefinitionId, StepId}` semantics.

### 4) Lifecycle synchronization
- Publish/Unpublish/Delete now enqueue scheduler sync tasks in DB transaction.
- Quartz updates are applied by background reconciler (idempotent retry model), not directly in use cases.

### 5) Legacy compatibility
- Legacy `WorkflowSchedule` path is preserved and used as fallback when definition has no embedded cron triggers.
- Startup bootstrap backfills Quartz state from current source-of-truth state.

## Operational notes
- New scheduler sync task persistence is required in DB.
- Restart all related services after deploy to pick up runtime behavior changes.

## Result
Cron scheduling is now deterministic, state-aware, and safer under retries and clustered startup.
