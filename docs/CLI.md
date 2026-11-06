# Command Line Reference

`WinTool-by-PcNinja.exe` runs the window with no arguments and the command
line with arguments (decision D-016). Output is English; add `--json` for
machine-readable output with stable camelCase names. Options are
`--name` or `--name value`; an unknown option is an error, never ignored.

The EXE is a Windows application, so `cmd.exe` does not wait for it. In
scripts use `start /wait`, or PowerShell `Start-Process -Wait -PassThru`, to
get the exit code. Scheduled tasks always wait.

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
| `schedule import-v2` | Yes | Takes over WinUpdate Tool V1/V2 on this computer: imports its settings, removes its tasks, registers WinTool's (D-015). |

`schedule set` options: `--enable`, `--disable`,
`--frequency Daily|Weekly|Monthly|Startup`, `--time HH:mm`,
`--day Sunday..Saturday`, `--month-day 1-28`, `--at-startup` / `--no-at-startup`,
`--startup-delay MIN`, `--run-if-missed` / `--no-run-if-missed`,
`--wake` / `--no-wake`, `--reboot-prompt` / `--no-reboot-prompt`,
`--firmware` / `--no-firmware`, `--auto-retry` / `--no-auto-retry`,
`--retry-delay MIN`, `--retry-attempts N`, `--retry-backoff N`, `--cooldown MIN`.

## Other

| Command | What it does |
| --- | --- |
| `status` | Version, computer, Windows, last update run, schedule, pending restart. |
| `logs collect [--output folder\|file.zip]` | Zip with logs, settings, state, task XML and status. Default: `%ProgramData%\PcNinja\WinTool\LogPackages`. |
| `version`, `help`, `--self-test` | Version, this summary, completeness check of the EXE. |

## Exit codes

| Code | `update run` | Other commands |
| --- | --- | --- |
| 0 | Updates installed, none needed, or the pass limit reached | Success |
| 1 | Failed | Failed |
| 2 | Usage error | Usage error (unknown command or option, invalid value) |
| 3 | Cancelled | — |
| 5 | — | Administrator rights required |
| 10 | Restart required | — |
| 11 | Completed, but some updates failed | Completed with warnings (health warnings, reset warnings) |
| 20 | Retry scheduled | — |
| 21 | Skipped: Windows busy, cooldown, or another WinTool operation running | Another WinTool operation is running |
| 22 | Needs attention: retry limit reached or automatic retry off | — |

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
