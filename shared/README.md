# Shared PcNinja components

Files in this folder are shared by every PcNinja Windows tool:

- Windows Upgrade Tool
- WinUpdate Tool V1 and V2
- Smart Office Installer
- the planned unified PcNinja tool

**Keep them byte-identical in every repository.** Change them in one place,
then copy the same file to the other repositories in the same change set. They
are written in C# 5 so that every build path can compile them unchanged:
.NET 8 SDK projects, .NET Framework 4.8 projects, legacy `csc.exe` v4 and
PowerShell 5.1 `Add-Type`.

## PcNinjaSingleInstance.cs

This file makes each GUI single-instance per Windows logon session. If the app
is launched while it is already open, no second copy starts. The running
window is restored if minimized and brought to the front.

| Call | Where it goes |
| --- | --- |
| `TryActivateExisting(appId, options, out outcome)` | An unelevated launcher, just before it self-elevates. `ActivatedExisting`: exit with code 0. `ExistingNotResponding`: tell the user and exit. `NoInstance` or `GuardUnavailable`: continue and elevate. This avoids a UAC prompt for an app that is already open. |
| `Acquire(appId, options, out outcome)` | The GUI process, after elevation and before any window or file extraction. `null` means exit: it is `ActivatedExisting`, or `ExistingNotResponding` (tell the user). Otherwise start the GUI, and dispose the holder as soon as the main window has closed. |

The one-argument overloads remain for simple callers. They return `null` or
`false` when the caller must exit.

Rules:

- **GUI only.** CLI and automation modes (BigFix, scheduled tasks, `-Mode Status`)
  must not be blocked by an open window. Protect shared machine operations
  with their own operation locks (for example the Windows Upgrade Tool's
  `AppOperationGate`).
- **Fixed app ids.** Changing an app id breaks detection between old and new
  builds, so never derive one from a version.

  | App id | Product |
  | --- | --- |
  | `WinUpgradeTool` | Windows Upgrade Tool |
  | `WinUpdateTool` | WinUpdate Tool V1 and V2. They share the id because they are the same tool with the same scheduled tasks and machine state. |
  | `SmartOfficeInstaller` | Smart Office Installer |

- **Fails open, and says so.** If the guard cannot be created, or the named
  object exists but is unusable (inaccessible, or a different object type),
  the outcome is `GuardUnavailable`. The app then starts without the guard and
  a diagnostic line is written. Set `DiagnosticSink` to route these lines to
  the app log. Starting is safer than silently refusing to open, and
  operation locks still prevent conflicting work.
- **No silent exit.** A second launch exits only after the running instance
  acknowledges that it focused its window, or after a not-responding timeout
  that the app reports to the user.
- **Self-update is unaffected.** The update helpers start the new GUI only after
  the old process has exited, and the kernel releases the guard when the old
  process exits or crashes.
- **Nested entry is handled.** An EXE host that runs the PowerShell UI
  in-process sets `PCNINJA_SINGLE_INSTANCE_HELD=<appId>|<pid>`. A second
  `Acquire` call in the same process then returns `NestedOwner`. Child
  processes do not match, because the marker includes the process id.

### How it works

1. **Two named events per app and session.** The request event is
   `Local\PcNinja.<AppId>.SingleInstance.v1` and the acknowledgement event is
   the same name with `.Ack` appended. The owner creates both and keeps them for
   its lifetime.
2. **A later launch (the requester) signals and waits.** It calls
   `AllowSetForegroundWindow`, signals the request event, and waits up to 15 s
   for the acknowledgement. It re-signals every second, so concurrent launches
   are never starved.
3. **The owner acknowledges only after a real activation.** It restores and
   focuses its own main window, or the modal dialog that window owns. If
   Windows refuses the focus change, it flashes the taskbar button. When no
   window exists yet (startup), it retries for up to 10 s and then
   acknowledges.
4. **The owner closes while a requester waits.** The requester notices that
   the request event is gone, takes ownership, and starts normally.
5. **Old owners are still reached.** An owner without the `.Ack` event (a build
   from before acknowledgements existed) is signalled once, as before. Both
   directions remain compatible.
6. **Unelevated launches reach elevated owners.** The event DACLs let
   interactive users wait on and signal the events. Their integrity label is
   Medium. Because the owner activates only its own window, UIPI restrictions
   between elevated and standard processes do not apply. If custom security
   is rejected, default security is used, and an unelevated launch may then
   need UAC before it can reach the owner.

### Unified tool

The unified tool should compile this same file and call `Acquire` once in
its GUI entry point with a new fixed id (for example `PcNinjaToolkit`).

The open question is whether the unified tool may run next to the
standalone tools:

- **No.** At startup it also probes the legacy ids (`WinUpgradeTool`,
  `WinUpdateTool`, `SmartOfficeInstaller`) with `TryActivateExisting`.
- **Yes.** No change is needed.

Decide this when the unified tool's design is closed.
