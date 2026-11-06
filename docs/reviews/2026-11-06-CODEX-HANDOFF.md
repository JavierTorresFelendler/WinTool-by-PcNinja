# Codex review and Claude coordination handoff

## Owner request and review baseline

The owner asked Codex to review WinTool, add useful recommendations, and help
Claude distribute work across Claude, Codex and the locally installed GitHub
Copilot. Claude should remain the coordinator and first investigate how to
delegate from its actual environment. The aim is less duplicated context and
less work charged to a single assistant, not a promise of lower total usage.

Reviewed baseline: `5c7321e86b34b213cbf997fddc22a986a422d11f` on `main`.
This is an initial targeted review of lifecycle, operation locking, Office,
Dashboard integration, CLI boundaries and existing tests, not an exhaustive
audit of every ported engine or proof of release readiness. Recheck findings
against the current head before assigning fixes.

## Findings

### F1 [P1] Closing the GUI abandons supervision of an active installer

Evidence: `src/WinTool.App/App.xaml:4` uses `OnMainWindowClose`;
`src/WinTool.App/Views/MainWindow.xaml.cs:51-54` closes unconditionally.
Neither the window nor `App.OnExit` has an operation-aware closing guard.
`src/WinTool.Office/OfficeDeployment.cs:130,141` starts an external installer
and waits asynchronously. Its operation lease exists only in the WinTool
process. Windows Upgrade similarly awaits an external Setup process.

Trigger: start an Office install/removal, then close the main window (caption
button or Alt+F4). WPF shuts down while the external process can continue.
When the last handle to the named operation semaphore disappears, a subsequent
WinTool process can create a fresh gate and start another changing operation.
The managed wait, final result and cleanup also cannot finish after exit.

Recommendation: centralize closing policy for all changing operations. While
installation is uninterruptible, keep the process and lease alive; explain
why closing is blocked or hide the window while monitoring continues. For
cancellable preparation, cancel and await actual completion before closing.
Use the existing localization resources. Do not kill Windows Setup or Office
to implement closing. A separately supervised worker would be a larger design
choice and is not required for the first fix.

Verification: use fake long-running work to test close/Alt+F4, cancellation,
natural completion, and failure. In an isolated test, verify another process
remains busy until the supervised operation really ends. Real installation
tests belong on a disposable VM, never on the development machine. This
finding is based on source tracing; no real installation was started.

### F2 [P2] Remove-first confirmation depends on successful Office detection

Evidence: `src/WinTool.App/ViewModels/OfficePageViewModel.cs:310` requires
`installed != null` before asking for remove-first confirmation, but line 326
still sets `RemoveExisting` from the checkbox alone. Its Office detection
converts read errors into null. `OfficeInfo.ReadCurrent` only describes
Click-to-Run Office. `OfficeConfiguration.Build` emits the remove-all element
whenever `RemoveExisting` is true. The CLI already requires `--confirm`
regardless of detection.

Trigger: Office detection fails or yields incomplete data, the user selects
remove-existing, and presses Start. The remove-first request proceeds without
the additional confirmation promised by the Office behavior specification.

Recommendation: confirmation should depend on the requested destructive
action, not on the informational detection result. Retain the unconditional
confirmation for remove-all mode. Distinguish failed detection from a known
absence where practical.

Verification: a view-model test with null/failed detection and remove-existing
selected must request confirmation; rejecting it must make no deployment call.

### F3 [P2] The generated offline launcher always prints "Done"

Evidence: `src/WinTool.Office/OfficeDeployment.cs:206-225`, `InstallScript`,
runs `setup.exe /configure Office_Config.xml` and then unconditionally prints
"Done". It does not branch on the installer result or explicitly preserve and
return that result. The direct GUI/CLI deployment paths do check the result.

Trigger: an offline installation returns nonzero, including a missing or
corrupt setup executable. The generated launcher still displays "Done".

Recommendation: capture the result immediately, display a truthful outcome
and the numeric code, and explicitly return the saved code after any pause.
Keep the existing package layout. OF-34 records the upstream script as kept;
record this intentional fix in the behavior/sync documentation when applied.

Verification: run a generated launcher against a harmless stub that exits 42;
check the failure text and final exit code. A success stub should report
success. No Office execution is needed for this regression test.

## First task for Claude: investigate delegation, then run one small trial

1. Read `AGENTS.md`, `docs/DECISIONS.md`, and the current worktree status.
   Identify the authoritative checkout and the changes currently in progress.
2. Inventory only the relevant local interfaces: Codex CLI version/help/auth
   availability, and whether GitHub Copilot is an IDE extension, its CLI, or
   both. Check PATH and a small number of known installation locations. An IDE
   extension alone is not evidence that a `copilot` executable is available.
   Never copy or print authentication tokens. Existing GitHub CLI login does
   not prove either assistant is authenticated or entitled to run.
3. Determine whether this Claude session can execute local commands. A cloud
   Claude session may not be able to reach the owner's installed applications.
   Do not assume local access from the commit author/session URL. If it cannot,
   use short task files and owner-mediated handoff; do not claim automation.
4. Check the installed CLI help and official documentation before choosing
   commands. Codex supports noninteractive `exec`; GitHub documents Copilot
   programmatic `-p`/`--prompt`. Use only options supported by the installed
   versions. Start with read-only permissions and a tiny scoped review.
   No blanket approval/sandbox bypass, automatic installation, account changes,
   or paid API provisioning are needed for the investigation.
5. Return a compact feasibility result: detected interface/version, access
   constraints, exact validated invocation, output capture, timeout, and any
   required owner action. Distinguish local help checks from an actual model
   request. Verify the plan/usage source in the UI or official account tooling;
   do not assume API billing or subscription quotas from the executable name.
6. Once authenticated and available, run at most one small trial task per
   worker. Capture output in files. If an interface is unavailable, report it
   and use the working interface; do not spend repeated prompts troubleshooting
   without a concrete lead. Stop and surface a login/approval/quota failure.

Observed by Codex in this review: `codex --help` and `codex exec --help` work;
`Get-Command copilot` found nothing on this process's PATH, and `gh extension
list` was empty. This does not establish that Copilot is absent. No new model
session, Copilot prompt, installation or credential change was initiated.

Example Codex read-only invocation, after validating local help and paths
(PowerShell; this creates a separate CLI run, not a continuation of the open
Codex desktop chat):

```powershell
# Set these to the dedicated worker checkout and task/result files first.
Get-Content -Raw -LiteralPath $taskFile |
    & codex -a never exec -C $workerPath -s read-only -o $resultFile -
```

Permission settings do not replace the task scope: instruct the worker not to
run an installer, update/reset Windows, modify schedules or reboot. Do not
grant general shell execution to Copilot just to make the first trial pass.

Official references checked for this handoff:

- [Codex noninteractive mode](https://learn.chatgpt.com/docs/non-interactive-mode)
- [Running GitHub Copilot CLI programmatically](https://docs.github.com/en/copilot/how-tos/copilot-cli/automate-copilot-cli/run-cli-programmatically)

## Suggested division of work

| Owner | First bounded assignment | Integration responsibility |
| --- | --- | --- |
| Claude | Triage F1-F3 against current code; own the lifecycle design and ongoing feature integration | Maintain the task list, resolve overlaps, review worker patches, run integrated checks |
| Codex | Independently verify one finding and its focused regression coverage; implement only after Claude assigns specific files | Return evidence, small diff/commit and exact check results |
| Copilot | Audit English/Hebrew resource-key parity and stale README/architecture/roadmap status; initially report only | Return concrete missing keys or stale statements with paths; no speculative rewrite |

F1 and F2 both affect application view models: do not assign overlapping files
to different workers simultaneously. F3 can be a separate Office-library task
after Claude reserves its files. Retain Claude ownership of accepted product
decisions and unresolved integration choices.

Use separate branches and worktrees/checkouts for editing workers. Do not
share one writable working tree or switch branches underneath another worker.
Pin every task to a commit, list allowed files, and let Claude cherry-pick or
apply reviewed changes. If Claude has uncommitted changes, supply an explicit
snapshot/diff; a clean `main` checkout does not include those changes.

Start with at most two workers, one bounded task each, and one review pass.
No recursive delegation or repeated full-repository audits. Keep logs on disk;
send only the final short result and relevant error excerpts to the coordinator.
Usage must be measured before claiming savings. Preserve successful sessions
for closely related follow-ups when useful; do not preload entire chat histories.

### Task packet template

```text
Task ID / base commit / assigned agent:
Objective (one behavior):
Read first (only the necessary paths):
Allowed edits / files owned by another worker:
Acceptance check / failure and edge cases:
Do not run: real Setup, Office install/remove, Windows updates/reset,
            schedule changes, MSI install/uninstall, or reboot on this PC.
Output: <=400 words: outcome; files/commit; checks actually run and results;
        unresolved risk or blocker. No push/merge/release unless assigned.
Stop after the scoped result; do not delegate again.
```

Track only task ID, owner, base commit, allowed files, status, result path and
review disposition in the coordinator's existing task record. Add infrastructure
only if the small trial proves that it helps.

## Verification record

The reviewed baseline has successful GitHub CI runs
[42248525857](https://github.com/JavierTorresFelendler/WinTool-by-PcNinja/actions/runs/42248525857)
and
[42247748935](https://github.com/JavierTorresFelendler/WinTool-by-PcNinja/actions/runs/42247748935).
These do not cover the missing lifecycle/view-model cases above.

Local checks on Windows:

- Release solution build: passed, zero warnings and zero errors.
- Release solution tests: 590 passed, zero failed, zero skipped across net48
  and net8.0-windows (Core 71, Office 34, Upgrade 13, WindowsUpdate/CLI 177,
  repeated for each framework). The 13 Upgrade entries group additional
  assertions; this number is the test-runner count, not a count of scenarios.
- `git diff --check`: passed for the documentation changes.
- Test result files remain outside the checkout in `../wintool-review-results`.

The checkout requests SDK 8 through `global.json`, but this host has SDK 9 and
10, not SDK 8. Running `dotnet --version` inside the checkout therefore fails.
The successful commands below were launched from its parent directory and used
installed SDK 10.0.403 without editing `global.json`. Restore emitted an SDK-8
resolution diagnostic from a child invocation, but the overall test command
exited zero and all eight TRX results confirm the counts above. This is
supplementary local validation; the linked CI uses the requested SDK 8.

```powershell
# Run from the parent directory of wintool-review.
dotnet test ./wintool-review/WinTool.sln -c Release --logger 'trx;LogFilePrefix=review' --results-directory ./wintool-review-results -v minimal
dotnet build ./wintool-review/WinTool.sln -c Release --no-restore -v minimal
```

No new regression tests or runtime code were added in this documentation-only
handoff. No real Office or Windows installation, Windows Update/reset, MSI
installation, schedule mutation, reboot, GUI lifecycle test or VM test was run.
The findings above remain source-based and need focused regression coverage.
