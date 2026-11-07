# Architecture

WinTool by PcNinja is a WPF application on .NET Framework 4.8 (see
[DECISIONS.md](DECISIONS.md)). This document describes the target structure.
Nothing below is implemented yet.

## Solution layout

```text
src/
  WinTool.App/              WPF executable: shell window, navigation, views,
                            view models, CLI entry point
  WinTool.Core/             Shared services: logging, settings, atomic file
                            writes, elevation, operation locks, OS checks,
                            localization, app self-update
  WinTool.WindowsUpdate/    Windows Update engine (WUA COM), schedule
                            (Task Scheduler), health check, reset
  WinTool.Upgrade/          Windows upgrade engine (ported from
                            PcNinja.Win11Upgrade.Core)
  WinTool.Office/           Office deployment engine (ported from Smart Office
                            Installer, uses the Office Deployment Tool)
tests/
  WinTool.Core.Tests/
  WinTool.WindowsUpdate.Tests/
  WinTool.Upgrade.Tests/
  WinTool.Office.Tests/
packaging/
  msi/                      WiX source, setup pages, license RTF
  portable/                 Single-file portable build
shared/                     Files shared byte-identical with other PcNinja tools
docs/
```

### Dependency rules

```text
WinTool.App ──► WinTool.WindowsUpdate ─┐
            ├─► WinTool.Upgrade ───────┼──► WinTool.Core
            └─► WinTool.Office ────────┘
```

- Feature modules depend only on `WinTool.Core`, never on each other.
- Feature modules never reference WPF. They expose plain services that the
  GUI, the CLI and the tests all call the same way.
- Every machine-changing operation (install updates, upgrade, Office install
  or removal, Windows Update reset) runs under the machine-wide operation
  lock from `WinTool.Core`. Two such operations never overlap, whether they
  come from the GUI, the CLI, or a scheduled task.

## Navigation

Each tool is one screen that holds everything needed to operate it, so the
user never switches tabs to work with one tool. The Dashboard is read-only:
it summarizes what each tool sees on this computer and links to the tool
(owner decisions, 2026-11-06). The Drivers page from WinUpdate V2 is not
included.

```text
Dashboard          Summary only: pending updates and restart, Windows
                   version and upgrade path, installed Office edition and
                   its apps, computer details (name, user, LAN/WAN IP, CPU,
                   memory, storage, graphics, board, BIOS, TPM, last
                   restart), WinTool status
──────────
Windows Update     Status and restart state, available updates, health and
                   maintenance actions, and the schedule (edited in place)
Windows Upgrade    This computer, installation source, Windows image,
                   options, validate and start. ISO help: find a
                   matching ISO, PcNinja ISO downloads, Microsoft page
Office             Product family, mode (install / offline package /
                   remove), channel, apps, languages, summary
──────────
Logs               Lazy-loaded, filtered by tool
Settings           Theme, language, application updates, about
Useful Links       help.pcninja.pro, PcNinja free tools (Driver Updater,
                   Windows & Office Activation, www.pcninja.pro, this
                   repository), shared file password, manufacturer
                   sources (Dell, HP, Lenovo, ASUS; reference only)
```

A Help link to help.pcninja.pro is always visible in the sidebar footer.

Footer: installed version, latest version, **Check Tool Update**.

## Command line

One CLI grammar for all modules, which the scheduled task also uses:

```text
WinTool-by-PcNinja.exe <area> <command> [options] [--json]

WinTool-by-PcNinja.exe update scan
WinTool-by-PcNinja.exe update install [--include-optional] [--no-reboot]
WinTool-by-PcNinja.exe update reset
WinTool-by-PcNinja.exe schedule show
WinTool-by-PcNinja.exe schedule set --frequency Monthly --day 15 --time 03:00 ...
WinTool-by-PcNinja.exe schedule remove
WinTool-by-PcNinja.exe upgrade status | scan | validate | run
WinTool-by-PcNinja.exe office status | install --product ... | remove
WinTool-by-PcNinja.exe app-update check | install
WinTool-by-PcNinja.exe status [--json]
WinTool-by-PcNinja.exe logs collect
```

Exit codes are documented per command. `--json` output is stable and
versioned, so scripts can depend on it.

## Cross-cutting rules carried over from the existing tools

- UTF-8 everywhere. Update titles in Hebrew and other scripts must never be
  garbled (WinUpdate V2.2.7).
- Configuration and state files are written atomically: write a temporary
  file, then replace (WinUpdate V2.2.6).
- Self-update downloads to a temporary file, verifies SHA-256 and the expected
  file name, and only then replaces anything (WinUpdate V2.2.6, Upgrade Tool,
  Smart Office).
- GUI single instance per logon session; CLI is never blocked by an open
  window (`shared/README.md`).
- Theme follows Windows dark/light mode live; colors come only from theme
  tokens (D-012).
- No hidden console window and no reliance on an attached console in the GUI
  (WinUpdate V2.2.8 incident).
