# Protected portable scheduling: revision plan

Status: proposed design, not implemented. D-027 records the owner's accepted
product choice; only its explicitly stated behavior is accepted. Codex leads
reconciliation. Independent review is relayed through the owner.

Base implementation reviewed by FABLE 5.2:
`e56082b3cf61b1052273854dfb6398d06dbe0350` (draft PR #13), based on main
`8fdbd561a490f5a4ee0251be7ef6b43fdab53d1e`.

## Confirmed behavior

- Portable scan/update, Windows upgrade and Office functionality stay available
  without MSI installation. Default automatic-retry settings must not prevent
  a portable update run from starting.
- A SYSTEM task must use an explicitly validated local protected executable;
  neither portable path equality nor an MSI registry entry establishes that
  protection by itself. USB and ordinary writable paths are never task targets.
- Scheduling from a portable copy provisions a dedicated protected local copy.
  It creates no Start menu entry or MSI product registration.
- Removing the last relevant schedule/task retires the managed executable.
  An executable still used by an active operation is not deleted underneath it.

## Proposed implementation contract

1. **Classify launch identity separately from task eligibility.** Preserve the
   centralized MSI identity helper for app-update/chooser behavior. Introduce
   a separate task-target resolver for an installed protected EXE or a managed
   protected task copy. Validate owner, effective permissions, ancestors,
   filesystem identity and reparse/hard-link boundaries; a custom MSI directory
   must satisfy the same protection requirements. Normalize 8.3 aliases through
   verified file identity, without accepting look-alike prefixes.
2. **Provision locally and atomically.** Keep the managed copy under WinTool's
   own protected machine-data root. Its exact root depends on the separate S1
   decision. Stage and verify executable identity, source and copied bytes,
   version and permissions before publishing a generation or changing a task.
   Do not assume the current pathname still denotes the running image. No
   registry value or command-line flag may bypass task-target validation.
3. **Preserve the portable GUI choice.** Installation remains available. The
   alternative explains that a protected local scheduler copy is retained
   without MSI installation. Cancel makes no schedule change. Update English,
   Hebrew and Spanish resources; never show a hard-coded CLI message in the
   window. An unavailable MSI must not silently select another action.
4. **Keep the CLI noninteractive.** An explicit scheduling command may provision
   the protected task copy and reports the resulting target. It does not offer
   an interactive MSI prompt. Inspection/removal remain available from portable
   copies. Document the retained local files and the failure/cleanup behavior.
5. **Keep manual runs independent of scheduling success.** Remove the early
   refusal based on `AutoRetryEnabled`. At the point a retry is actually needed,
   resolve/provision a protected task target. If that fails, report the existing
   needs-attention/not-scheduled outcome with the reason; do not claim a retry
   exists or invalidate the preceding run merely because scheduling is unavailable.
   Preserve actual update outcomes and the existing exit-code meanings.
6. **Handle all task types and generations together.** Main schedules, automatic
   retries and run-once SYSTEM tasks use the same target policy. Provisioning,
   registration, rollback, update/replacement and retirement use operation locking
   and durable state. Retain the old verified generation if publishing or task
   registration fails. Never clean up a generation still referenced by another
   task or active operation. Define deferred cleanup explicitly before coding;
   create no persistent cleanup service merely for this feature.
7. **Separate worker behavior from manual launch behavior.** A managed task copy
   runs without an Install/Portable dialog. It must not silently take the MSI
   updater path just because it is protected, or silently replace itself while
   tasks/operations still use it. Reconcile scheduler-copy update behavior with
   the existing consent-based app-update contract before implementation.

## Evidence and pre-flight

Classification: Complex. Do not implement the whole proposal before proving a
small file-only prototype in isolated temporary paths. The prototype must show
source/copy identity verification, atomic generation publication, refusal of
unsafe targets and rollback after injected failures. Use fakes for task services
and active-process lifetimes. No real task registration or production root is
allowed on the development PC.

Then pin the prototype/plan SHA for FABLE 5.2 review with bounded read-only scope.
S1 findings are a dependency: a protected copy cannot rely on an unsafe data
parent, in-place adoption of untrusted objects, or ungated environment overrides.
Keep S3's effective test-variable gate when reconciling changes.

## Acceptance checks

- Portable default `update run` reaches its normal fake execution path; it is
  not refused solely because automatic retry is enabled.
- GUI Install, protected-copy choice and Cancel have correct localized results.
- USB/Downloads sources yield a local protected task target; changing or removing
  the original file cannot change what a previously registered task runs.
- An MSI custom path is accepted only if its EXE and parent chain are protected.
- Invalid identity/ACL/owner/reparse/hard-link targets are refused before task
  registration. Existing settings/tasks survive a failed preparation step.
- Retry/run-once use the same protection policy. Preparation failure at retry
  time reports no scheduled retry with the correct existing outcome/exit code.
- Removing tasks cleans up only managed, unreferenced, inactive generations;
  cancel/failure/concurrent-operation cases leave recoverable, explicit state.
- Build with warnings as errors, all unit tests on both targets, code review,
  complete CI, then owner tests on a designated disposable VM with a snapshot.
  VM tests include source USB removal/drive-letter reuse, standard-user write
  refusal, custom MSI path, reboot persistence, upgrade and active-run removal.

## Current limitations

No implementation, file-only prototype or new runtime validation is claimed by
this document. The earlier green CI covers the previous implementation, which
does not yet satisfy D-027. PR #13 remains Draft. Merge, release, publication and
development-PC system changes remain unauthorized.
