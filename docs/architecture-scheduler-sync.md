# Architecture – Scheduler Sync (Quartz + Reconciler)

## Goals
- Keep Quartz state synchronized with workflow lifecycle (Publish/Unpublish/Delete).
- Prevent stale task side effects.
- Maintain compatibility for legacy schedule data.
- Work safely in multi-host runtime.

## Components

### 1) Use cases (producer side)
- `PublishDefinitionUseCase`
- `UnpublishDefinitionUseCase`
- `DeleteDefinitionUseCase`

Behavior:
- Persist business state first.
- Enqueue `WorkflowSchedulerSyncTask` in the same transaction.
- Do **not** call Quartz directly from use cases.

### 2) Sync task queue (DB)
- Entity: `WorkflowSchedulerSyncTask`
- Operation enum retained for audit (`Publish`, `Unpublish`, `Delete`).
- Runtime handling is reconcile/state-aware.

### 3) Reconciler
- Service: `WorkflowSchedulerSyncReconcilerService`
- Polls due tasks, reads current definition/schedule state, applies projection to Quartz.
- Uses distributed lock key: `workflow-scheduler-sync-reconciler`.
- Retries with backoff on failure.

### 4) Bootstrap
- Service: `QuartzScheduleBootstrapService`
- Runs at startup to reconcile existing definitions into Quartz.
- Uses distributed lock key: `workflow-scheduler-bootstrap`.
- Per-definition error isolation; failures can enqueue retry tasks.

### 5) Projector (single source of truth)
- Entry point: `CronScheduleSyncHelper.ProjectDefinitionToQuartzAsync(...)`

Rules:
1. `definition == null` or `!definition.IsPublished` -> delete Quartz group.
2. If definition has embedded `CronTrigger` -> sync embedded cron.
3. Else if active legacy `WorkflowSchedule` exists -> sync legacy schedules.
4. Else -> delete Quartz group.

This prevents bootstrap and reconciler drift.

## Cron execution model
- Quartz uses one-shot triggers.
- Next occurrence is calculated via Cronos.
- Job reschedules itself after firing.

## Concurrency model
- Reconciler and bootstrap are independently serialized via distributed locks.
- This reduces multi-host delete/rebuild races without requiring row-claim migrations.

## Failure model
- Task processing is idempotent and state-aware.
- Stale retries reconcile to **current state**, not old intent.
- Failed items are retried by queued tasks.
