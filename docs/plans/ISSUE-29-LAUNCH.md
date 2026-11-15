# Issue #29: installed-copy-aware universal GUI launch

Planning only; owner approved D-038, not implementation. Target after 1.0.3;
do not modify or rebuild published assets. Independent FABLE review comes next,
followed by reconciliation and separate owner implementation authorization.

## Evidence and existing constraints

Base: main7485036c297aae92cd93abd915fa417f22cbd352 (published1.0.3).
`Program.Run` dispatches CLI before GUI, reads per-user UI settings, probes or
acquires single-instance ownership, and invokes `AskInstallOrPortable` before
machine-data migration and elevated task maintenance.
`AskInstallOrPortable` already calls `FindInstalledCopy` and `InstalledCopy.Decide`
BEFORE the dialog. The defect is ignoring RunInstalled until the Install answer;
do not implement a second lookup on the mistaken assumption none exists.
`InstalledCopy.Decide` already handles self/same/newer/older decisions. Existing
`InstalledCopyTests` are the pure-policy test surface.
Current Process.Start failure for a same/newer install falls back to MSI. Thus
an observed reinstall could follow that logged failure, not just version logic.
No separate defect is confirmed without reproduction/logs.

AppLog and UI settings are touched early. A GUI version fence before migration
does not mean no filesystem access occurred. Do not claim compatibility for old
already-shipped EXEs, which this feature cannot change.

## Approved target table (no running instance)

| Discovery | Default universal GUI launch |
| --- | --- |
| No installation | Existing Install / Run portable / Cancel |
| Protected install, same version | Start installed copy; no chooser |
| Protected install, newer version | Start installed copy; no older portable GUI |
| Protected install, older version | Update X to Y? Yes installer; No installed copy; window close cancels |
| This is the installed copy | Existing behavior, no chooser |
| Missing/unreadable registered executable | Record diagnostic; safe not-installed choice; no automatic repair |
| Unprotected target | Explain and fall back to choice; never reuse RunInstalled to execute it |

Existing running-window activation takes precedence. In particular an already
running older copy is activated, not silently stopped for an update prompt.
`--portable` does not create a second instance or bypass existing owner rules.
For known newer installations, explicit portable GUI is blocked as well.

## Proposed implementation boundaries for review

1. Separate read-only discovery/trust facts from a pure launch decision. Reuse
   registry identification and `ProtectedMachineData.IsProtectedExecutable`;
   do not substitute a Program Files prefix. Test lookup failures and malformed
   version/path facts. Revalidate executable protection immediately before
   execution; privileged path/source is not a user-provided argument.
2. Classify a lone `--portable` as an explicit GUI request before generic CLI
   dispatch. Existing CLI commands never ask or redirect. Mixed/unknown option
   forms fail usage without a window or installer. The internal portable-chosen
   marker is UI context, not authority: direct invocation cannot bypass the
   newer-version or protection checks. Preserve the test-environment UAC guard.
3. Preserve single-instance activation/guard lifecycle. Hand off and promptly
   dispose the launching parent's guard; do not wait for the child GUI to exit
   while holding it. Validate startup under ordinary-user and already-elevated
   entry; no new arbitrary path or environment forwarding across UAC.
4. Resolve default redirection before migration/task maintenance. Avoid loading
   the normal main view model for a launch that must redirect or stop. Basic
   logging/per-user localization already happen earlier; identify precisely
   which compatibility boundary the fence protects.
5. Propose an error with no implicit MSI repair if opening a trusted installed
   copy fails. The new default launch contains no installer consent. Preserve
   installer invocation only for explicit Install/Update consent. Re-read facts
   when necessary after UAC; no hidden redirect retry/install loop.
6. Add localized en/he/es update, blocked-portable, untrusted-target and failure
   text using existing resources. Keep startup/instance commands and ordinary
   portable availability on non-installed machines intact.

## Review blockers to settle before implementation

- Unsafe-target fallback: define exact buttons/actions. It must not inherit the
  current same/newer RunInstalled result after a dialog answer. Do not silently
  change a custom install directory or repair its ACL. An older carried MSI must
  not be used as an automatic downgrade. If safe choice semantics need MSI or
  elevation changes, stop and bring the extra scope to the owner.
- Version-fence scope: the approved target is the new GUI launch flow. Existing
  CLI/data guards and self-update restart remain unchanged by scope; verify
  their ordering and do not promise that old released binaries or arbitrary CLI
  uses are prevented. If the owner needs a global data-compatibility mechanism,
  propose it separately rather than slip it into this change.
- Self-update restart: must stay noninteractive. Define whether it preserves
  deliberate portable intent or exits when discovery changes, with fresh trust/
  version checks. No silent installation or bypass through restart markers.
- Cancellation/UAC failure: no fallback to a lower-trust target, no false success
  for failure to launch; define exit code/log semantics and avoid credential data.

## Allowed future edit surface (not permission to edit now)

Expected: `Program.cs`, a small app startup-policy adapter if needed,
`InstalledCopy.cs`, `InstalledCopyTests.cs`, app resource files in all three
languages, `docs/CLI.md`, and decisions documentation. Confirm app-layer test
seam during design review; there is currently no app test project. Do not claim
Core tests alone prove WPF/UAC behavior.
No edits to packaging/MSI, AppUpdater, release feed, operation locks, protected
data implementation, schedule ownership, shared/, or elevation model. Reuse
existing protection APIs; do not refactor them to make this feature pass.

## Verification budget after implementation authorization

- Pure unit table: absent/self/same/newer/older, explicit portable, unsafe target,
  missing/unreadable metadata and the required version fence. Use fresh discovery
  facts; fail closed for launch failures without implicit installer execution.
- Startup routing tests with fake effects: existing CLI never asks/redirects;
  lone portable flag, malformed flag combinations, internal marker, restart,
  activation precedence, trust changes and launch failure. Do not run real
  installers/services from unit tests. Validate the chosen app test seam first.
- Normal full CI; exact-head independent code review before one VM visit.
- One disposable snapshot VM visit: clean/uninstalled choice, same-version
  protected install (no prompt/no installer), newer universal over older install
  (explicit update consent and No opens installed copy). Include a version-fence
  negative case and unsafe-path refusal only as minimal startup fixtures; no
  Windows Update install, Office/Setup run or scheduling campaign.
- Reuse prior product tests within unchanged scope. No arbitrary trial count or
  release authority. Stop on unexpected mutation, trust bypass or installer run.

## Deliverable now

Docs-only planning PR, pinned base/head, D-038 owner decision, source corrections,
explicit blockers and review packet. No product code, strings, tests, binaries,
installer or VM changing operation in this planning task.
