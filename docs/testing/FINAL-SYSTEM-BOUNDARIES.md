# Final bounded coverage check

Owner authorization: 2026-11-14, D-033. No merge, tag or release.
Keep the reviewed RC7 executable and MSI unchanged. The helper/test/documentation
head is separate from binary source `b58c036`; production `src/` is unchanged.
Wait for independent exact-head helper review before running on the VM.

## Automated migration check

`Busy_migration_preserves_inputs_and_retries_once_after_the_lease_is_available`
tests both the production-shaped `OperationBusyException` acquisition failure
and the core method's null-lease case, using isolated temporary files/delegates.
While busy, no destination exists, no validation/write happens and legacy bytes
remain unchanged. When the lease is available, preparation/validation/writes all
occur while held; validated data and report precede the completion marker.
A later completed call neither acquires nor reimports changed legacy input.
Existing failure/retry and completed-marker tests remain in place.

This does not claim a real production semaphore collision, process restart, or
GUI `deferWhenBusy` catch was executed. No application refactor is added merely
for a test; the core retry and durability contract is the new automated coverage.

## Disposable VM only: two boundaries, one visit

Prepare a clean pre-WinTool snapshot and a real standard-user account. Copy old
evidence out first. Extract the kit to `C:\WinTool-SystemVM`; keep network
disconnected throughout. Do not start any task or open the GUI. Setup/Finish
run elevated in 64-bit Windows PowerShell 5.1; StandardUser runs in the actual
standard account, not an administrator with a filtered token.

1. Setup installs baseline 1.0.2, then passively waits up to 120 seconds for
   installer-started WinTool workers to exit (three consecutive one-second
   observations with none present). It never kills a process, acquires the
   worker's operation gate or retries a failed step. A timeout stops Setup and
   returns evidence. It then creates a future schedule and disables the task.
   It initializes RC7 data, then seeds a synthetic last-finished timestamp and
   a 1440-minute cooldown, with auto retry off. This is test input, not a prior
   successful update. The real newer portable command is
   `update run --scheduled --json`: the command's normal maintenance entry runs,
   then UpdateRunner must return `CooldownActive`/21 before pending-reboot,
   network/service checks or Search/Download/Install. A COM Update Session is
   constructed by CLI; no native update operation is invoked on this path.
   Offline networking is an additional guard, not evidence of a successful run.
   Assert unchanged protected Program Files task XML and Scheduler file hashes,
   no task refresh errors or new native query/install/service-start markers.
   The captured log must contain the exact CLI `Command: update run --scheduled
   --json` line followed by `Cooldown active`, not just an exit code.
   Exit 21 with `AlreadyRunning` is NOT a pass for this test.
2. Setup normally uninstalls the baseline, then installs the RC7 MSI into a
   lab-only folder with Users Modify. It creates another future, disabled SYSTEM
   task after the same bounded passive installer-worker wait. That wait observes
   WinTool processes only; it does not prove an otherwise idle Windows machine.
   Its action must point at a protected managed copy matching RC7, never
   the writable installed source. If MSI hardens the source so it is not writable,
   the intended writable-source fixture is unavailable, not silently counted.
3. StandardUser opens the actual source EXE for writing without writing bytes;
   that must succeed. Opening the protected target for writing and creating a
   file beside it must fail with access denied. It records account SID and
   refuses local Administrators membership. This phase changes no EXE bytes.
4. Finish checks the matching standard-user record, source/target hashes and
   task action, then normally uninstalls RC7 and checks no own task remains.
   Return the single Finish ZIP containing all phases. On FAIL, stop and return
   the current phase ZIP; do not start a task or retry blindly. MSI 3010 is a
   review condition here, not silently treated as exit 0 or an automatic reboot.

The helper sets only lab ACLs, installs/uninstalls the two exact MSI fixtures,
creates/removes disabled WinTool tasks, and seeds fresh lab machine data.
No Reset, Snooze, Windows/Office installation or scheduled task execution.
It never deletes ProgramData or old evidence. Restore the clean snapshot after
copying evidence out. Standard-user proof is owner-run evidence, not a claim of
tamper-proof attestation from an adversarial account.

## Commands after exact-head review PASS

```powershell
cd C:\WinTool-SystemVM
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Final-SystemBoundary.ps1 -Phase Setup -ConfirmDisposableVM -ConfirmReviewedHelpers -ConfirmOffline -SnapshotLabel (Read-Host 'Actual restored clean snapshot name')
```

After Setup passes, switch to the standard account:

```powershell
cd C:\WinTool-SystemVM
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Final-SystemBoundary.ps1 -Phase StandardUser -ConfirmDisposableVM -ConfirmReviewedHelpers -ConfirmOffline
```

After StandardUser passes, return to administrator:

```powershell
cd C:\WinTool-SystemVM
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Final-SystemBoundary.ps1 -Phase Finish -ConfirmDisposableVM -ConfirmReviewedHelpers -ConfirmOffline
```

No prior A/B/Cancel/search/history/signature campaign is repeated. These are new
boundary cases, not a full lifecycle rerun. D-033 deferrals and production-file
release gates stay explicit; helper Setup success is not overall VM acceptance.
