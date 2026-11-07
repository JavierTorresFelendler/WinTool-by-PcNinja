# Command Line Reference

`WinTool-by-PcNinja.exe` runs the window with no arguments and the command
line with arguments (decision D-016). Output is English; add `--json` for
machine-readable output with stable camelCase names. Options are
`--name` or `--name value`; an unknown option is an error, never ignored.

The EXE is a Windows application, so `cmd.exe` and PowerShell do not wait
for it: the prompt comes back at once and the output arrives after it
(decision D-020). To wait and get the exit code:

```bat
start /wait WinTool-by-PcNinja.exe update run
```

```powershell
Start-Process .\WinTool-by-PcNinja.exe -ArgumentList 'update','run' -Wait -NoNewWindow -PassThru
```

Scheduled tasks and the MSI always wait.

## Windows Update

| Command | Admin | What it does |
| --- | --- | --- |
| `update scan [--windows] [--optional] [--drivers] [--firmware]` | No | Lists available updates and counts; installs nothing (WU-08). Without scope options: everything except firmware, unless firmware is enabled in the settings. |
| `update run [--scheduled\|--retry\|--startup\|--wake] [--snooze] [scope options]` | Yes | Searches, downloads and installs, up to 3 passes (WU-20). Without a run type it is a manual run. `--snooze` (manual only) stops a busy Windows Update and continues (WU-31). |
| `update reset --confirm` | Yes | Stops the update services, rebuilds SoftwareDistribution, moves catroot2 aside, starts the services (WU-70). |
| `update health [--no-network]` | No | The health check (WU-95). |
| `update start-as-system` | Yes | Starts a one-time run as SYSTEM now (WU-56). |

## Schedule

| Command | Admin | What it does |
| --- | --- | --- |
| `schedule show` | No | Settings, task state, next run, whether the task matches the settings. |
| `schedule set [options]` | Yes | Changes only the options given, validates, registers the task, then saves. Nothing is saved when anything fails. |
| `schedule remove` | Yes | Turns the schedule off and removes WinTool's tasks. |
| `schedule import <file>` | Yes | Applies a WinTool settings file, or a WinUpdate Tool `config.json`. |
| `schedule import-v2 [--if-present] [--keep-schedule] [--wait-for-installer]` | Yes | Takes over WinUpdate Tool V1/V2: imports its settings (unless `--keep-schedule`), removes its tasks, registers WinTool's schedule and uninstalls it (D-015, D-025). Exit 1 with "Incomplete" when the uninstall fails. `--if-present` succeeds quietly when there is nothing to take over. |

`schedule set` options: `--enable`, `--disable`,
`--frequency Daily|Weekly|Monthly|Startup`, `--time HH:mm`,
`--day Sunday..Saturday`, `--month-day 1-28`, `--at-startup` / `--no-at-startup`,
`--startup-delay MIN`, `--run-if-missed` / `--no-run-if-missed`,
`--wake` / `--no-wake`, `--reboot-prompt` / `--no-reboot-prompt`,
`--firmware` / `--no-firmware`, `--auto-retry` / `--no-auto-retry`,
`--retry-delay MIN`, `--retry-attempts N`, `--retry-backoff N`, `--cooldown MIN`.

## Windows Upgrade

The commands of the Windows Upgrade Tool, on the same Core as the window
(`docs/specs/WINUPGRADE-BEHAVIOR.md`). They never restart the computer after a
failure, and the command line never asks a question.

| Command | Admin | What it does |
| --- | --- | --- |
| `upgrade status` | No | Windows, upgrade eligibility, download profile, system drive space, previous upgrade attempt. Offline. |
| `upgrade downloads` | No | Recommends a matching download page. Downloads nothing, opens nothing. |
| `upgrade scan` | No | Local ISO files beside the EXE, mounted Windows media, download drives. |
| `upgrade validate --iso <file> \| --mounted <drive-or-folder> [--sha256 HASH] [--image-index N]` | Yes | Validates one source without starting Setup. `--iso` mounts that exact file. |
| `upgrade run --iso <file> \| --mounted <path> \| --download [--destination folder] [--sha256 HASH] [--image-index N] [--no-restart] [--quiet] --yes\|--dry-run` | Yes | Validates, then starts Windows Setup. Without `--yes` nothing starts (exit 2); `--dry-run` validates only. |

A drive or folder that ends in a backslash must not be quoted: in cmd.exe
`"E:\"` reaches the program as `E:"`. Write `--mounted E:\`, or
`--mounted "C:\Win11 Extracted"` (no trailing backslash) when the path has spaces.

`--sha256` (or the `PCNINJA_ISO_SHA256` environment variable) compares the ISO
with a trusted hash. `--no-restart` adds `/noreboot`; restart yourself when
ready. `--quiet` hides the Setup window only.

With `--json` the result keeps the Windows Upgrade Tool's contract
(schemaVersion 1): `resultCode`, `exitCode`, `success`, `message`,
`readyForRestart`, `logPath`, `version`, `build`, plus the command's own
fields with their original names. **A script may restart the computer only
when** `upgrade run --no-restart --yes --json` returns exit 0,
`resultCode` `UPGRADE_PREPARED_RESTART_REQUIRED` and `readyForRestart: true`.
Every other result, including `VALIDATION_PASSED` and `DRY_RUN_COMPLETED`
with exit 0, is not permission to restart.

| resultCode | Exit |
| --- | --- |
| `STATUS_REPORTED`, `DOWNLOAD_RECOMMENDATION`, `SOURCES_SCANNED`, `VALIDATION_PASSED`, `DRY_RUN_COMPLETED` | 0 |
| `UPGRADE_PREPARED_RESTART_REQUIRED`, `UPGRADE_PREPARED_AUTOMATIC_RESTART` | 0 |
| `INVALID_ARGUMENTS` | 2 |
| `CANCELLED` (Ctrl+C before Setup starts) | 3 |
| `ADMINISTRATOR_REQUIRED` | 5 |
| `OPERATION_RUNNING` (another WinTool operation) | 21 |
| `VALIDATION_FAILED` (unsupported computer, media, storage) | 30 |
| `DOWNLOAD_FAILED` | 31 |
| `SETUP_FAILED` (`setupExitCode` holds Setup's code) | 32 |
| `SETUP_OUTCOME_UNCONFIRMED` (check after restarting) | 33 |
| `UNEXPECTED_ERROR` | 1 |

Moving from the Windows Upgrade Tool: commands gain the `upgrade` prefix,
`upgrade` became `upgrade run`, `--yes` is always needed to start Setup, and
the exit codes are WinTool's (D-021). The JSON fields and `resultCode` values
are unchanged, so scripts that read them keep working.

## Office

Install, package or remove Office with Microsoft's Office Deployment Tool
(ODT). WinTool downloads the newest ODT from Microsoft each time and runs it
only when it is signed by Microsoft (D-022). Once the Office installer has
started it is never interrupted; Ctrl+C before that, or during a package
download, cancels.

| Command | Admin | What it does |
| --- | --- | --- |
| `office status` | No | The installed Click-to-Run Office: product, version, platform, channel, apps. |
| `office config [options] [--quiet]` | No | Prints the configuration file the options produce. Changes nothing. |
| `office install [options] [--quiet] [--remove-existing --confirm]` | Yes | Installs Office. `--remove-existing` first removes every installed Office and needs `--confirm`. |
| `office package --output <folder> [options]` | Yes | Downloads Office into `<folder>\OFFICE-OFFLINE` with `setup.exe`, `Office_Config.xml` and `Install-Office.bat`. Copy that folder to the target computer and run `Install-Office.bat` as administrator. |
| `office remove --confirm [--quiet]` | Yes | Removes every Click-to-Run Office product. |
| `office tool-check` | No | Finds the newest Office Deployment Tool, downloads it and checks the Microsoft signature, then deletes it. Runs nothing. Exit 0: found on Microsoft's page and signed; 11: signed, but from WinTool's fixed address; 40/41: not downloaded / not signed. |

Options: `--family m365|ltsc` (default `m365`: Microsoft 365 Apps for
Enterprise; `ltsc`: Office LTSC Professional Plus 2024),
`--channel Current|MonthlyEnterprise|SemiAnnual` (Microsoft 365 only),
`--apps Word,Excel,PowerPoint,Outlook,OneNote,Access,Publisher,Teams,OneDrive`
(default the first five), `--languages en-us,he-il,...` (the first is the
primary language; default `en-us,he-il`), `--visio`, `--project`, `--x86`
(32-bit), `--no-activate` (LTSC: do not activate the volume license during
installation). `--quiet` hides the Office installer window.

```bat
start /wait WinTool-by-PcNinja.exe office install --family ltsc --languages he-il,en-us --visio --quiet
```

## WinTool itself

| Command | What it does |
| --- | --- |
| `app-update check [--test-versions] [--json]` | Is a newer WinTool published on GitHub? Test versions only with `--test-versions` (D-023). |
| `app-update install --yes [--test-versions] [--json]` | Portable: downloads and verifies the new EXE and replaces this one; the old one stays as `WinTool-by-PcNinja-v<file version>.exe` (D-018). Installed: downloads and verifies the MSI and prints the `msiexec` command to run once WinTool is closed. |
| `version [--json]` | Version; with `--json` also the file and MSI versions (D-024). |

## Other

| Command | What it does |
| --- | --- |
| `status` | Version, computer, Windows, last update run, schedule, pending restart. |
| `logs collect [--output folder\|file.zip]` | Zip with logs, settings, state, task XML and status. Default: `%ProgramData%\PcNinja\WinTool\LogPackages`. |
| `version`, `help`, `--self-test` | Version, this summary, completeness check of the EXE. |

## Test environment variables

For testing only; WinTool works without them.

| Variable | Effect |
| --- | --- |
| `WINTOOL_DATA_ROOT` | An absolute folder. WinTool keeps its machine data (logs, Windows Update and upgrade state, downloaded updates, log packages) in `<root>\ProgramData` and its user settings in `<root>\User`, instead of `%ProgramData%\PcNinja\WinTool` and `%LocalAppData%\PcNinja\WinTool`. The log's first line says so. A relative or empty value is ignored. It does not move the old WinUpdate Tool's configuration, scheduled tasks, the registry or the installed program. |
| `WINTOOL_UPDATE_FEED` | Another releases JSON (https or a local file) for `app-update` and the update question; files beside a local JSON are used as the downloads. |

## Exit codes

| Code | `update run` | Other commands |
| --- | --- | --- |
| 0 | Updates installed, none needed, or the pass limit reached | Success |
| 1 | Failed | Failed |
| 2 | Usage error | Usage error (unknown command or option, invalid value) |
| 3 | Cancelled | Cancelled (`upgrade`) |
| 5 | — | Administrator rights required |
| 10 | Restart required | — |
| 11 | Completed, but some updates failed | Completed with warnings (health warnings, reset warnings) |
| 20 | Retry scheduled | — |
| 21 | Skipped: Windows busy, cooldown, or another WinTool operation running | Another WinTool operation is running |
| 22 | Needs attention: retry limit reached or automatic retry off | — |
| 30–33 | — | Windows Upgrade results, see above |
| 40 | — | Office: the Office Deployment Tool could not be downloaded or extracted |
| 41 | — | Office: the Office Deployment Tool is not signed by Microsoft; nothing ran |
| 42 | — | Office: the Office installer failed (`setupExitCode` holds its code) |

## Examples

```bat
start /wait WinTool-by-PcNinja.exe update run --json
```

```bat
WinTool-by-PcNinja.exe schedule set --enable --frequency Weekly --day Sunday --time 03:00 --wake
```

```powershell
$p = Start-Process WinTool-by-PcNinja.exe -ArgumentList 'update','scan','--json' -Wait -PassThru -NoNewWindow -RedirectStandardOutput scan.json
$p.ExitCode
```
