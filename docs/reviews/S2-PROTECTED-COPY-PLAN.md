# Protected portable scheduling: revised candidate

Status: implemented candidate, awaiting FABLE 5.2 re-review before owner VM
testing. D-027 accepts the product behavior; D-028 accepts the root. Neither
decision establishes VM acceptance. Base: main
8fdbd561a490f5a4ee0251be7ef6b43fdab53d1e. This revision supersedes the earlier
installed-only runtime at e56082b3cf61b1052273854dfb6398d06dbe0350.

## D-027 coverage requested by the owner

**(a) Yes:** normal schedules, automatic retry and update start-as-system from
portable copies share the protected-target resolver. Default manual updates
start normally even with automatic retry enabled. Target preparation occurs
when retry is needed; failure returns needs-attention / exit 22 without
claiming that a retry exists.

**(b) Yes:** an MSI registry entry does not establish task eligibility. An EXE
in an MSI folder writable by non-administrators is not used directly: provision
the protected copy, or refuse task creation if preparation fails. MSI identity
remains separate for the chooser and MSI updater.

## Implementation and lifecycle

1. Check owner/ACL, ancestors, reparse and hard-link boundaries before using a
   source directly. Otherwise provision the official EXE under
   `%ProgramData%\PcNinja-WinTool\Scheduler\<SHA256>\`, without MSI registration
   or shortcuts.
2. Open the running image without write/delete sharing; compare its mapped
   native path with the opened file and validate product/version. Hash pinned
   bytes, stage a protected file, verify hash/ACL and publish atomically.
   Existing generations must match their hash and protection. Preparation
   failure precedes removal of any existing task.
3. GUI Yes retains installation; No provisions the protected copy; Cancel makes
   no schedule change. Failed installation does not silently select No.
   English/Hebrew/Spanish resources explain choices/failure. CLI scheduling
   remains noninteractive; task XML records the resolved target.
4. Copy lifecycle and task mutation use the operation lock. Cleanup inspects
   all WinTool task references and removes only unreferenced managed images.
   Unknown task XML defers cleanup. Busy images retain retirement.pending;
   cleanup retries on a later administrative task change or elevated manual start. There is no cleanup
   service: an active copy can therefore remain until that later action.
   Empty pinned directories can remain until a subsequent cleanup.
5. Managed workers bypass the chooser and cannot self-update or use the MSI
   updater. MSI install/upgrade automatically refreshes all existing main/retry/
   run-once task targets while preserving their XML settings. Elevated manual GUI
   starts queue the same refresh, without creating a main task for retry-only
   installations. Old managed workers never refresh newer task targets. Older
   manual versions cannot downgrade a newer protected worker. Status exposes
   worker file versions and content mismatch with the running image. Busy GUI
   startup waits in the background for up to 15 minutes; closing the process or
   timing out defers to the next launch. Manual CLI starts attempt immediate
   refresh; portable CLI self-update starts the new verified EXE with the waiting
   refresh command, so provisioning never uses the old mapped process image.
   Installer maintenance waits up to 15
   minutes and fails explicitly if still busy; uninstall does not ignore failure.
   Cleanup respects references and active images. Run-once releases its operation
   scope before starting the SYSTEM task.
6. MSI identity expands existing 8.3 aliases without granting task eligibility.
   No registry value or user flag bypasses target protection.

## Evidence and remaining checks

File-only prototypes/unit fixtures check publication, source independence/reuse,
corrupt-generation refusal, failed-publish cleanup and busy/reference retirement
in temporary folders. Fake task tests cover portable schedule/run-once targets,
default manual runs and retry failure without false state. They never run WUA
or register real tasks.

CI checks portable registration uses protected local bytes, plus existing
installed monthly scheduling. It deliberately does not run a real
update start-as-system, which would start servicing on the build runner.

After independent review, owner VM checks must cover USB removal/drive-letter
reuse, actual running-image identity, standard-user write refusal, writable
custom MSI folders, 8.3 launch aliases, GUI Yes/No/Cancel/failure, retry/run-once,
reboot persistence, active-worker removal, deferred cleanup and worker refresh.
Include a Defender scan of the actual protected-copy provisioning and SYSTEM
registration flow (D-009 heuristics), not just the EXE file. Test task-service
failures/crash recovery: registration is not transactional
across delete/register. Preparation failure preserves tasks, but later scheduler
failure can require explicit reapplying of the schedule.

S1/PR #12 supplies migration/log compatibility. Consolidate the identical Core
protection primitive during integration. Retain PR #14's variable gate and
PR #15's complete verifier; reconcile SDK, CLI-test and workflow overlaps on one
candidate before VM acceptance. No shared/ changes, merge, tag or release.
