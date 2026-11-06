# Roadmap

Each phase ends with a review by the owner before the next one starts.
Versions before 1.0 are internal previews.

## Phase 0: Foundation documents (current)

- Decisions, architecture, MSI specification, localization rules, upstream
  sync log, license.
- Mockup of the unified window based on the approved PcNinja Dashboard design,
  in dark and light themes, English and Hebrew.

## Phase 1: Skeleton (0.1)

- Solution and projects as in `ARCHITECTURE.md`, building on Windows CI.
- WPF shell: Nocturne-based theme, grouped sidebar, empty pages, light/dark
  assets, high-DPI checks.
- Core services: logging, settings, atomic writes, OS version gate,
  elevation, operation lock, single instance (`WinTool`).
- Localization infrastructure with English and Hebrew resources.
- Theme service: follows the Windows dark/light setting live; dark and light
  palettes; Settings override (System, Light, Dark).
- Portable EXE build.

## Phase 2: Windows Update (0.2)

- `docs/specs/WINUPDATE-BEHAVIOR.md`: behavior inventory of the V2 engine.
- C# engine on the WUA COM API: scan, preview, install, restart state, health
  check, reset. Tests for every item in the spec.
- Schedule service with retry, wake-to-run and run-if-missed.
- CLI `update` and `schedule` commands.
- Home, Updates and Schedule pages.
- Answer Q-1 and Q-2 (portable scheduling, coexistence with V2).

## Phase 3: MSI (0.3)

- Interactive setup pages and silent properties as in `MSI-AND-CLI.md`.
- Install, upgrade, repair and uninstall tests on a VM.

## Phase 4: Windows Upgrade (0.4)

- Port `PcNinja.Win11Upgrade.Core` from .NET 8 to .NET Framework 4.8.
- Upgrade page and CLI `upgrade` commands.

## Phase 5: Office (0.5)

- Port the Smart Office Installer logic.
- Office page and CLI `office` commands.

## Phase 6: Release readiness (0.9)

- One self-update flow for MSI and portable installs (D-018: portable keeps
  the previous copy as `WinTool-by-PcNinja-v<version>.exe`).
- Spanish translation.
- Release workflow that publishes only tested packages, with SHA-256 hashes.
- VM test checklist across Windows 10 1903, Windows 10 22H2 and Windows 11,
  including 1024x768 and 1366x768 at 125% (D-019).

## Phase 7: 1.0

- Public release and README links from the existing tools.
