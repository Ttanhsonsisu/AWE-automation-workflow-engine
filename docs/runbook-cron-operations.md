# Runbook – Cron Scheduler Operations

## Scope
Operational checklist for cron workflow scheduling, reconciliation, and troubleshooting.

## Services involved
- API host(s) and worker host(s) that call `AddWorkflowEngineService()`.
- Quartz scheduler.
- Scheduler sync reconciler service.
- Startup bootstrap service.

## Lock keys
- Reconciler: `workflow-scheduler-sync-reconciler`
- Bootstrap: `workflow-scheduler-bootstrap`

## Deployment checklist
1. Apply DB migration(s) for scheduler sync task storage.
2. Deploy binaries to all hosts.
3. Restart services (API + engine workers) in controlled order.
4. Verify logs:
   - One host acquires bootstrap lock, others skip.
   - Reconciler ticks acquire lock serially.

## Health checks (manual)

### Publish flow
- Publish a definition.
- Confirm sync task is enqueued.
- Confirm reconciler processes task.
- Confirm Quartz job(s) created for definition.

### Unpublish flow
- Unpublish definition.
- Confirm Quartz group for definition is removed.
- Restart host and verify bootstrap does not recreate schedules for unpublished definition.

### Delete flow
- Delete definition.
- Confirm Quartz group removed.
- Confirm stale retries do not recreate schedule.

## Troubleshooting

### Symptom: Cron logs publish but no workflow instance starts
Check:
1. Routing key and consumer bindings.
2. Trigger type matching (`CronTrigger` / `CronTriggerPlugin`).
3. Job data (`StepId`) and start-node filtering.
4. Worker logs for consumer failures.

### Symptom: Schedules disappear after retries
Check:
1. Reconciler uses projector/state-aware path.
2. Definition current publish state.
3. Legacy schedule fallback eligibility.

### Symptom: Startup race across hosts
Check:
1. Bootstrap lock acquisition logs.
2. Multiple hosts trying to rebuild same Quartz group.

## Recommended test scenarios
1. Stale unpublish retry after republish should not delete new schedules.
2. Bootstrap error on one definition should not stop later definitions.
3. Multi-host startup should allow only one bootstrap owner.
4. Delete + stale retry should keep Quartz group deleted.

## Rollback strategy
- Keep reconciler and bootstrap logs enabled.
- If severe issue appears:
  1. Pause traffic to mutating workflow endpoints.
  2. Stop extra hosts and run single-host mode temporarily.
  3. Reconcile Quartz state using current DB source of truth.
