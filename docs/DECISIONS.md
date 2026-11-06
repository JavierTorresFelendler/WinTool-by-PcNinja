# Decision Log

Architecture decisions for WinTool by PcNinja. Each entry records the context,
the decision, and its consequences. A decision changes only by adding a new
entry that supersedes the old one; entries are never rewritten.

Status values: **Accepted**, **Proposed** (waiting for the owner), **Superseded**.

---

## D-001 One product that replaces three tools

- **Status:** Accepted (2026-11-06)
- **Context:** PcNinja ships three separate Windows tools: WinUpdate Tool V2
  (PowerShell + C# host), Windows Upgrade Tool (.NET 8 WinForms), and Smart
  Office Installer (.NET Framework 4.8 WinForms). Each one has its own UI,
  logging, self-update, single-instance guard and release pipeline.
- **Decision:** Build one application, **WinTool by PcNinja**, in this
  repository. It covers Windows Update management, Windows upgrade, and Office
  installation.
- **Consequences:** Shared concerns (UI shell, logging, settings, self-update,
  elevation, operation locks) are written once. The existing tools are not
  modified by this project and keep their own release lines (see D-005).

## D-002 Runtime: .NET Framework 4.8, x64, Windows 10 1903 or later

- **Status:** Accepted (2026-11-06)
- **Context:** .NET Framework 4.8 is built into Windows 10 1903+ and every
  Windows 11 release, so it needs no runtime installation. A self-contained
  .NET (Core) build adds roughly 70 MB to the portable EXE. .NET 8, which the
  Upgrade Tool uses today, reaches end of support on 2026-11-10.
- **Decision:** Target `net48` with SDK-style projects and a recent C# language
  version. Supported OS: Windows 10 1903 or later and Windows 11, x64 only.
- **Consequences:**
  - Small EXE that runs on a fresh or damaged machine with nothing to install.
  - C# features that need runtime support (for example default interface
    methods) are unavailable; normal modern syntax works.
  - Code ported from the .NET 8 Upgrade Tool must replace .NET 8-only APIs.
  - Startup refuses unsupported OS builds with a clear message.

## D-003 UI: WPF with MVVM, Nocturne-based dark theme

- **Status:** Accepted (2026-11-06)
- **Context:** The approved design (PcNinja Dashboard, Nocturne design system)
  uses rounded cards, soft shadows, translucent accents and a sidebar. In
  WinForms each of these needs custom painting and has known high-DPI issues.
  WPF ships with .NET Framework 4.8.
- **Decision:** WPF with the MVVM pattern. Views contain no business logic.
  Theme colors, spacing and typography live in one resource dictionary that
  mirrors the Nocturne tokens.
- **Consequences:** The Upgrade and Office screens are rebuilt in WPF; their
  logic is ported, not their WinForms code. ViewModels can be tested without a
  window.

## D-004 Rewrite the Windows Update engine in C#

- **Status:** Accepted (2026-11-06)
- **Context:** The V2 engine is about 12,000 lines of PowerShell across about
  180 functions. It calls the Windows Update Agent COM API
  (`Microsoft.Update.Session`) and also shells out to `UsoClient` and
  `wuauclt`. The owner considers it the most cumbersome of the three code
  bases. It also carries hard-won fixes (Hebrew/UTF-8 titles, scheduled task
  de-duplication, the V2.2.7 console encoding incident).
- **Decision:** Rewrite the engine in C# against the WUA COM API. Before any
  code, write `docs/specs/WINUPDATE-BEHAVIOR.md`, an inventory of every
  capability and edge case of the V2 engine. Every item in that spec gets a
  test or a written reason for being dropped.
- **Consequences:** No PowerShell runtime dependency, no encoding layer between
  processes, and direct testability. The V2 engine remains the reference for
  behavior until the new engine passes the VM checklist.

## D-005 Existing repositories stay untouched; changes are ported by log

- **Status:** Accepted (2026-11-06)
- **Context:** Development continues in the three source repositories while
  this project is built. Because code here is a rewrite, automatic code sync
  (submodule, subtree) is not possible.
- **Decision:** `docs/UPSTREAM-SYNC.md` records, for each source repository,
  the last reviewed commit. Every newer commit is reviewed and marked
  **ported**, **not applicable**, or **pending**. A CI job will report source
  commits that have not been reviewed.
- **Consequences:** No fix made in an existing tool is silently lost. This
  project never pushes to the source repositories.

## D-006 Interface languages: English, Hebrew, Spanish

- **Status:** Accepted (2026-11-06)
- **Decision:** All UI text lives in `.resx` resources from the first commit.
  English ships first. Hebrew (right-to-left) and Spanish are added before
  1.0. See `docs/LOCALIZATION.md`.
- **Consequences:** Small cost per screen when it is built; retrofitting later
  would cost much more.

## D-007 Packaging: MSI (interactive and silent) and portable EXE

- **Status:** Accepted (2026-11-06)
- **Decision:** Ship a per-machine x64 MSI built with WiX and a single portable
  EXE. The interactive MSI shows setup pages, including the Windows Update
  schedule page. Every option on those pages is also available as a public MSI
  property for silent installs. BigFix is not a target deployment method. See
  `docs/MSI-AND-CLI.md`.
- **Consequences:** The MSI gets a new UpgradeCode and does not upgrade the
  WinUpdate Tool V2 MSI in place (see open question Q-2).

## D-008 Freeware license

- **Status:** Accepted (2026-11-06)
- **Decision:** Use the PcNinja Freeware Use License that the Windows Upgrade
  Tool already uses: official binaries are free to use, including on computers
  the user is authorized to administer. Copying, modifying, redistributing or
  selling the code or a derivative requires written permission.
- **Consequences:** This is not an open-source license. This is not legal
  advice; review the text with a lawyer before a commercial launch.

## D-009 Releases are unsigned for now

- **Status:** Accepted (2026-11-06)
- **Decision:** Releases ship unsigned. Each release publishes SHA-256 hashes,
  and the self-updater verifies them. The build keeps a signing step that
  is skipped while no certificate is configured.
- **Consequences:** SmartScreen warnings are expected. The portable EXE must
  avoid patterns that look suspicious to antivirus software (lesson from
  WinUpdate V2.2.5: no embedded archive that drops and runs another EXE).

## D-010 One single-instance guard for the whole application

- **Status:** Accepted (2026-11-06)
- **Decision:** Use the shared `PcNinjaSingleInstance.cs` with the fixed app id
  `WinTool`. The guard applies only to the GUI. CLI and scheduled runs use
  operation locks so that two machine-changing operations never run at the
  same time.
- **Consequences:** The copy in `shared/` is taken from the Windows Upgrade
  Tool, which has the newest version (with acknowledgements). The Smart Office
  Installer copy is older and differs, although `shared/README.md` requires
  identical copies. That gap belongs to the Smart Office repository.

---

## Open questions

| Id | Question | Owner decision |
| --- | --- | --- |
| Q-1 | Can the portable EXE create a Windows Update schedule? A scheduled task needs a stable path. Proposal: the portable EXE offers to install itself (run the MSI) before scheduling. | Pending |
| Q-2 | Machines that already have WinUpdate Tool V2 have their own scheduled tasks (`PcNinja WinUpdate Tool`, `... Retry`, `... Run Once`). Proposal: WinTool detects them and offers to take over the schedule and uninstall V2, so two tools never update the same machine. | Pending |
| Q-3 | One EXE for both GUI and CLI (as in the Upgrade Tool), or a separate console EXE in the MSI for cleaner scheduled-task output? Proposal: one EXE, as the Upgrade Tool already does. | Pending |
