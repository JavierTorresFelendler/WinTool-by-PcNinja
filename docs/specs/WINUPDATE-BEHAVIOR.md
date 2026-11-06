# Windows Update Engine: Behavior Specification

This document is the inventory of everything the WinUpdate Tool V2 engine
does (decision D-004). The C# engine in `WinTool.WindowsUpdate` is written
against it. Every item has an id. Each item is one of:

- **Keep**: same behavior; a test proves it.
- **Change**: same purpose, better behavior; the reason is given.
- **Drop**: not carried over; the reason is given.
- **Owner**: needs a decision from the owner before it is built.

Source: `PcNinja-WinUpdateTool-V2` at `323af0a` (V2.2.8), mainly
`WinUpdateCore.psm1` (3,644 lines) and `WinUpdateTool.ps1` (4,689 lines).
Line numbers refer to that commit.

---

## 1. Searching for updates

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-01 | Uses the Windows Update Agent COM API (`Microsoft.Update.Session`) with `ClientApplicationID` set to the product name. | Core 3109 | **Keep**. ClientApplicationID becomes `WinTool by PcNinja`. |
| WU-02 | Registers Microsoft Update (service id `7971f918-a847-4430-9279-4a52d1efe18d`, flags 7) before every search, so Office and other Microsoft products are offered. Failure is a warning, not an error. | Core 671 | **Keep**. Check once per run, not per pass. |
| WU-03 | Three searches per pass, merged: broad `IsInstalled=0 and IsHidden=0`, drivers `... and Type='Driver'`, optional `... and BrowseOnly=1`. A failed search is logged and skipped; the pass fails only when all three fail. | Core 3113 | **Keep**. Windows sometimes omits drivers or optional updates from the broad search, which is why there are three. |
| WU-04 | Merge key: `UpdateID|RevisionNumber`; falls back to type and title. Each update remembers which searches found it. | Core 399, 462 | **Keep**. |
| WU-05 | Classification: Driver when `Type=2`; Optional when `BrowseOnly`; otherwise Windows (important). | Core 610 | **Keep**. |
| WU-06 | Firmware/BIOS detection: the title or a category matches `firmware`, `bios`, `uefi` or `system firmware`. | Core 426 | **Change**. Also use the driver class (`IWindowsDriverUpdate.DriverClass = Firmware`), which is more reliable than words in the title. Keep the word match as a fallback. |
| WU-07 | Scope: Windows, Optional, Drivers, Firmware. Without an explicit scope, Windows + Optional + Drivers are included and Firmware is not. | Core 587 | **Keep**. |
| WU-08 | Preview (`-Mode PreviewUpdates`): counts important, optional, drivers, firmware skipped, out-of-scope; lists title, KB, type, scope, sources. Installs nothing. | Script 886 | **Keep** as `update scan`. |
| WU-09 | Each found update is logged with title, KB list, type, categories, flags (BrowseOnly, AutoSelectOnWebSites, IsMandatory) and firmware flag. | Core 526 | **Keep**. |
| WU-10 | Text handling: update titles in Hebrew, Arabic and other scripts must stay readable in the window and in the logs (V2.2.7 fix). | Core 158, tests | **Keep**. In C# this needs no encoding layer; a test with Hebrew titles still guards it. |

## 2. Installing

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-20 | Up to 3 passes: search → download → install → search again. Stops early when nothing is left, when a restart is needed, or when an install fails. Result `MaxPassesReached` after the last pass. | Core 3093 | **Keep**. |
| WU-21 | Before each pass, a pending restart stops the run with `RebootRequired`. | Core 3094 | **Keep**. |
| WU-22 | Accepts the EULA of each queued update automatically. | Core 3168 | **Keep**. |
| WU-23 | Downloads all queued updates as one batch; continues when the result is Succeeded or SucceededWithErrors; installs only updates that are actually downloaded. | Core 3205 | **Keep**. |
| WU-24 | Download and install are synchronous, with no progress and no cancel. | Core 3208, 3247 | **Change**. Use the asynchronous API (`BeginDownload` / `BeginInstall`) for progress per update and a Cancel button. |
| WU-25 | Only the overall install result is logged. | Core 3248 | **Change**. Also log the result and HRESULT of each update (`GetUpdateResult(i)`), and show which ones failed. |
| WU-26 | After each pass, a verification search is logged. | Core 3270 | **Keep**. |
| WU-27 | Installs everything in scope; the user cannot pick single updates. | Core 3150 | **Change**. The Windows Update screen has a checkbox per update (approved mockup). Scheduled runs keep installing everything in scope. |
| WU-28 | Firmware is skipped unless enabled in settings or for that run. The UI warns before a run or schedule with firmware enabled ("reliable power"). | Core 3155; Script 3558, 4029, 4325 | **Keep**, including the warnings. |
| WU-29 | When a restart is needed and the run is interactive, shows "Updates have been installed. A restart is required…" if the reboot prompt is enabled. | Core 3286 | **Keep**. Shown in the app window, not a separate message box. |

## 3. Before a run (preflight)

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-30 | Detects Windows Update activity. Installing: `TrustedInstaller`, `TiWorker`, `WindowsUpdateBox`, `SetupHost` processes or a running TrustedInstaller service. Background: `MoUsoCoreWorker`, `UsoClient`, `wuauclt`, `MusNotification(Ux)` or active Windows/Microsoft BITS jobs. | Core 761 | **Keep**. |
| WU-31 | When Windows is busy: scheduled and automatic runs do not start and schedule a retry. A manual run asks the user: retry later, "snooze" (stop the update services, restart them, then run) or cancel. | Core 3504; Script 3921 | **Keep**. "Snooze" stays manual-only. |
| WU-32 | Network check: DNS lookup of `download.windowsupdate.com` and an HTTPS HEAD to `www.microsoft.com` (5 s). Scheduled runs schedule a retry when it fails; manual runs only warn. | Core 707, 3491 | **Keep**. |
| WU-33 | Starts the services `wuauserv`, `bits`, `UsoSvc`, `dosvc`, `cryptsvc` when they are stopped. | Core 743 | **Keep**. |
| WU-34 | Logs the .NET Framework version. | Core 1148 | **Drop**. WinTool itself requires .NET Framework 4.8, so the value is always known. |

## 4. Pending restart

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-40 | Restart pending when either key exists: `CBS\RebootPending`, `WindowsUpdate\Auto Update\RebootRequired`. | Core 1807 | **Keep**. |
| WU-41 | `PendingFileRenameOperations` counts only when a path touches servicing or drivers (WinSxS, servicing, SoftwareDistribution, catroot, drivers, DriverStore, INF, pending.xml, wu*, uso*, mus*). Other renames are only a warning, because many programs leave harmless entries. | Core 1706 | **Keep**. This avoids false "restart required" reports. |
| WU-42 | A pending computer rename counts as pending restart. | Core 1837 | **Keep**. |
| WU-43 | "Restart now" restarts at once with no warning to signed-in users. | Core 1858 | **Keep** (owner, Q-WU-3): restart immediately. The button already asks for confirmation in the app. |

## 5. Schedule

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-50 | One task `\PcNinja\PcNinja WinUpdate Tool`, built from XML: SYSTEM, highest privileges, ignore new instance, runs on battery, 6-hour limit, priority 7. | Core 1976 | **Keep** the settings. New name `\PcNinja\WinTool by PcNinja` (D-015 handles the old task). |
| WU-51 | Frequencies: Daily, Weekly (one day), Monthly (day 1–28, every month), Startup. Optional extra trigger at startup with a delay (default 15 min in config, 5 in the MSI). | Core 1890, 1960 | **Keep**. Use one default (5 min) everywhere. |
| WU-52 | "Run if missed" maps to `StartWhenAvailable`; "Wake computer" maps to `WakeToRun`. | Core 1990 | **Keep**. |
| WU-53 | Saving a schedule first removes every task whose name starts with the product name, in any folder, then registers the new one; it fails if an old task cannot be removed (no duplicates, V2 test). | Core 2079, 1483 | **Keep**, plus V1/V2 task names for the takeover (D-015). |
| WU-54 | The task runs `powershell.exe -File WinUpdateTool.ps1 -Mode RunUpdates -Silent -RunType Scheduled`. | Core 1987 | **Change**. The task runs `WinTool-by-PcNinja.exe update run --scheduled` directly. No PowerShell. |
| WU-55 | Cooldown: scheduled, startup and wake runs do not start within N minutes (default 5) after the previous run finished. Manual and retry runs ignore it. | Core 3438 | **Keep**. |
| WU-56 | "Run once" task: a one-time SYSTEM task started immediately, for remote or admin launches. | Core 2108 | **Keep** as `update run --as-system`. |

## 6. Automatic retry

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-60 | Retry when: the network is not ready, Windows is busy, or the error is one of `0x8024402C`, `0x8024401C`, `0x80244022`, `0x80072EE2`, `0x80072EFD`, `0x80072EFE`. | Core 3337 | **Keep**. |
| WU-61 | Delay = first delay × multiplier^(n-1), at most 360 minutes. Defaults: 5 min, ×2, 3 attempts. | Core 1457 | **Keep**. |
| WU-62 | A retry is a separate one-time task `... Retry`. A new non-retry run or a successful run removes it and resets the counter. | Core 1527, 3466 | **Keep**. |
| WU-63 | A failed download (result Failed or Aborted) ends the run as failed without a retry. | Core 3212 | **Change**. A failed download caused by a retryable error is retried like WU-60. |

## 7. Reset Windows Update

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-70 | Requires explicit confirmation (`-ConfirmReset` in the CLI, a dialog in the UI). | Script 1676 | **Keep**. |
| WU-71 | Stops `UsoSvc`, `wuauserv`, `bits`, `dosvc`, `cryptsvc`; deletes and recreates `%SystemRoot%\SoftwareDistribution` (refuses any other path); up to 3 attempts; restarts the services; result Succeeded or SucceededWithWarnings. | Core 947 | **Keep**. |
| WU-72 | `catroot2` is not reset. | Core 947 | **Change** (owner, Q-WU-2): with `cryptsvc` stopped, also rename `%SystemRoot%\System32\catroot2` to a dated backup, which Windows rebuilds, as in Microsoft's reset procedure. Refuses any other path. |
| WU-73 | The `-ForceReset` switch is accepted but does nothing. | Core 949 | **Drop**. |

## 8. Configuration, state and records

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-80 | Machine config `%ProgramData%\PcNinja\WinUpdateTool\config.json` and state `state.json`, written atomically; version-based migration of old defaults. | Core 1177–1340 | **Keep** the content, new folder `%ProgramData%\PcNinja\WinTool`. Import the V2 file on takeover (D-015). |
| WU-81 | State: last run started/finished/type/result/message, last success, retry scheduled/reason/count, restart required. | Core 1282 | **Keep**. Shown on the Dashboard and the Windows Update screen. |
| WU-82 | Run lock file with process id and a 6-hour stale timeout. | Core 1359 | **Change**. The machine-wide operation lock (`OperationLock`) replaces it; the system releases it if the process dies. |
| WU-83 | Every log line also goes to the Windows Application event log (source "PcNinja WinUpdate Tool"). Event ids are reused for different meanings (for example 1080–1086). | Core 1097 | **Change** (owner, Q-WU-4): keep the event log with source `WinTool by PcNinja`, one fixed event id per meaning, documented in a table. |
| WU-84 | Important message boxes play a soft alert sound. | Core 185 | **Drop** (owner, Q-WU-4). |
| WU-85 | Log collection: a zip with logs, config, state, exported task XML and a status JSON. | Script 1480 | **Keep** as `logs collect`, covering all WinTool modules. |
| WU-86 | Status (`-Mode Status`): tool, version, computer, user, admin, paths, config, state, tasks, pending restart, activity, recent log lines. | Script 359 | **Keep** as `status --json`. |

## 9. Command line and exit codes

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-90 | `RunUpdates` returns exit code 0 for 12 different outcomes, including "skipped" and "retry scheduled"; only a failure returns 1. | Script 1730 | **Change**. Distinct exit codes so scripts can tell outcomes apart: 0 updates installed or none needed, 10 restart required, 20 retry scheduled, 21 skipped (busy or cooldown), 1 failed, 2 usage error. Full table in the CLI reference. |
| WU-91 | `Configure` sets every schedule option from switches or a JSON file. | Script 1322 | **Keep** as `schedule set` (same options) and `schedule import <file>`. |

## 10. Health check

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-95 | The V2 "Health Check Summary" card always shows "Healthy"; nothing is actually checked. | V2Ui 1369 | **Change**. A real check: update services can start, free space on the system drive, pending restart, Windows Update activity, network check (WU-32), Microsoft Update registered, scheduled task matches the settings. |

## 11. Driver audit

| Id | V2 behavior | Source | WinTool |
| --- | --- | --- | --- |
| WU-99 | Driver inventory report (CSV, JSON, summary) with manufacturer recommendations. | Core 2348–3014 | **Drop** (owner, Q-WU-1). Driver updates through Windows Update stay (WU-03, WU-07); manufacturer links are on the Drivers & Links screen. |

---

## Owner decisions (2026-11-06)

| Id | Question | Decision |
| --- | --- | --- |
| Q-WU-1 | Drop the driver audit report? | Dropped. |
| Q-WU-2 | Also reset `catroot2`? | Yes. |
| Q-WU-3 | "Restart now": immediate or countdown? | Immediate, as in V2. |
| Q-WU-4 | Event log and alert sound? | Event log yes, sound no. |
