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

- **Status:** Superseded in part by D-011 (timing only) (2026-11-06)
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
- **Amended by D-026 (2026-11-07):** the released EXE is the universal EXE,
  which carries the MSI and asks Install / Run portable when opened by hand.
  The MSI is still published beside it for IT and silent deployment.

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

## D-011 Hebrew from the first screen; Spanish before 1.0

- **Status:** Accepted (2026-11-06). Supersedes the timing in D-006.
- **Decision:** Every screen ships in English and Hebrew (right-to-left) from
  the first build. Spanish is added in the release-readiness phase.
- **Consequences:** Right-to-left problems are found while each screen is
  built, not at the end.

## D-012 Theme follows Windows (dark and light)

- **Status:** Accepted (2026-11-06)
- **Context:** The approved design is dark. WinUpdate V1 had a manual
  Light/Dark choice, and its light theme used plain Windows system colors.
- **Decision:** The app reads the Windows app mode
  (`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`,
  value `AppsUseLightTheme`) at startup and switches live when the user
  changes it. Dark mode uses the approved Nocturne-based design. Light mode
  gets its own designed palette with the same PcNinja accents, not plain
  system colors. Settings offers **System** (default), **Light** and
  **Dark**. When Windows High Contrast is on, the app uses the system
  high-contrast colors.
- **Consequences:** Every color is a theme token; no screen may use a fixed
  color. Each screen is reviewed in both themes and both text directions.

## D-013 Single instance also covers the installer

- **Status:** Accepted (2026-11-06)
- **Decision:**
  - Windows Installer already allows only one installation at a time; a
    second one fails with error 1618. Scripts and the self-updater must treat
    1618 as "busy, try later", not as failure.
  - The MSI detects a running WinTool and asks to close it (interactive), or
    closes it cleanly (silent), before files are replaced.
  - The self-updater starts at most one installer, and only after the GUI and
    any running WinTool operation have finished.
  - While an install or update is running, WinTool refuses to start
    machine-changing operations.
- **Consequences:** One GUI, one installer, and one machine-changing
  operation at a time on each computer.

---

## D-014 Portable EXE offers installation before scheduling

- **Status:** Accepted (2026-11-06). Answers Q-1.
- **Decision:** A scheduled task needs a stable path. When the portable EXE is
  asked to create a Windows Update schedule, it offers to install WinTool
  (run the MSI) and creates the schedule from the installed copy.
- **Amended by D-026:** the universal EXE runs the MSI it carries (offline)
  instead of downloading it.

## D-015 WinTool takes over from WinUpdate Tool V2

- **Status:** Accepted (2026-11-06). Answers Q-2.
- **Decision:** When WinTool finds WinUpdate Tool V1 or V2 or their scheduled
  tasks (`PcNinja WinUpdate Tool`, `... Retry`, `... Run Once`), it explains
  the situation, offers to import the schedule settings, and offers to
  uninstall the old tool. Two tools never update the same machine.

## D-016 One EXE for GUI and CLI

- **Status:** Accepted (2026-11-06). Answers Q-3.
- **Decision:** `WinTool.exe` runs the GUI with no arguments and the CLI with
  arguments, as the Windows Upgrade Tool already does.

## D-017 Repository becomes public at the first release candidate

- **Status:** Accepted (2026-11-06)
- **Decision:** The repository stays private during development and becomes
  public when the first release candidate is published. The in-app link to
  the repository depends on this.
- **Note:** The shared file password shown on the Useful Links screen (formerly Drivers & Links) is
  public on purpose (it carries the PcNinja name).

## D-018 Official file names and how updates replace them

- **Status:** Accepted (2026-11-06). Updates the file name in D-016.
- **Decision:**
  - The executable is always `WinTool-by-PcNinja.exe` and the installer is
    always `WinTool-by-PcNinja.msi`. Release file names never contain a
    version; the version lives in the file properties.
  - **Portable update:** after the new EXE is downloaded and its SHA-256 is
    verified, the running copy is renamed with its version, for example
    `WinTool-by-PcNinja-v1.2.0.0.exe`, the new file takes the official
    name, and the app restarts. The previous version stays next to it
    (the same approach as Smart Office Installer).
  - **MSI:** standard Windows Installer behavior. A newer version upgrades
    in place. The same version opens maintenance (Repair or Remove). An
    older version is blocked with a message. See `docs/MSI-AND-CLI.md`.
  - **D-026:** `WinTool-by-PcNinja.exe` in a release is the universal EXE.
    A portable update downloads it like before; the restarted copy never
    asks Install / Run portable. The feed and asset names are unchanged.

## D-019 Works on small screens down to 1024x768

- **Status:** Accepted (2026-11-06)
- **Context:** Freshly installed computers often run at 1024x768 before the
  display driver is installed. 1366x768 laptops at 125% scaling leave about
  1093x582 usable pixels.
- **Decision:**
  - The window opens at 1280x800 when it fits, otherwise maximized. Minimum
    size is 760x520.
  - Below 1100 pixels wide the sidebar shows icons only, with the names as
    tooltips.
  - Every screen scrolls instead of cutting content. Each screen is checked
    at 1024x768 in both languages, in addition to the checks in D-012.
- **Consequences:** Layouts use wrapping and flexible columns, never fixed
  page widths.

## D-020 Command-line use stays on the one EXE; waiting is documented

- **Status:** Accepted (2026-11-06). Confirms D-016 after the first real-machine test.
- **Context:** `WinTool-by-PcNinja.exe` is a Windows (GUI) program, so cmd.exe
  and PowerShell do not wait for it: the prompt returns at once and the output
  arrives after it. Scheduled tasks and the MSI wait normally.
- **Decision:** No separate console program. Interactive and script use waits
  with `start /wait` (cmd.exe) or `Start-Process -Wait -PassThru` (PowerShell),
  as documented in `docs/CLI.md` and in `help`.

## D-021 Windows Upgrade commands use WinTool's exit codes

- **Status:** Accepted (2026-11-06). Owner's choice.
- **Context:** The Windows Upgrade Tool's exit codes (0-8) give other meanings
  to numbers WinTool already uses (its 3 is a failed validation, WinTool's 3 is
  cancelled).
- **Decision:** One scheme for every WinTool command. Shared meanings keep
  their numbers (0, 1, 2, 3 cancelled, 5 administrator, 21 busy); Windows
  Upgrade results get 30 blocked/validation failed, 31 download failed,
  32 Setup failed, 33 Setup outcome unconfirmed. The JSON result contract
  (schemaVersion 1, `resultCode`, `readyForRestart`, original field names) is
  kept exactly. The command line never prompts: starting Setup needs `--yes`.
- **Consequences:** Scripts written for the Windows Upgrade Tool that check
  exit codes must be updated; scripts that read `resultCode` keep working.
  Documented in `docs/CLI.md`.

## D-022 Office: latest ODT, verified; all languages; CLI now; same products

- **Status:** Accepted (2026-11-06). Owner's choices.
- **Context:** Smart Office Installer downloads a fixed 2025 build of the
  Office Deployment Tool (ODT); Microsoft says to always use the latest. It
  offers English and Hebrew only and has no command line.
- **Decision:**
  - WinTool finds the latest ODT on Microsoft's download page each time, and
    runs it only when it is signed by Microsoft. If the page cannot be read,
    it uses a fixed ODT address that is updated with each WinTool release
    (also signature-checked).
  - Every language the ODT supports is offered, with search; English and
    Hebrew are selected by default.
  - Office gets command-line commands in this phase (`office ...`), on the same
    library as the screen.
  - Products stay as they are: Office LTSC Professional Plus 2024 and
    Microsoft 365 Apps for Enterprise, each optionally with Visio and Project.
- **Consequences:** `docs/specs/OFFICE-BEHAVIOR.md` lists every behavior of
  the original tool and what changed.

## D-023 Self-update asks; stable channel with an opt-in for test builds; Spanish reviewed

- **Status:** Accepted (2026-11-06). Owner's choices.
- **Decision:**
  - WinTool checks GitHub for a newer version at startup (when the setting
    is on), shows what is new, and updates only after the user agrees.
    Nothing is replaced without consent.
  - Stable releases by default. A setting, "Also get test versions",
    includes beta and release-candidate builds.
  - Spanish: Claude translates now; the strings are marked "needs review"
    until a Spanish speaker chosen by the owner checks them before 1.0.
    **Done (2026-11-07):** the owner, a native speaker, reviewed the
    Spanish; the markers were removed for 1.0.1.
  - Order in phase 6: the Logs and Drivers & Links pages first, then
    self-update and the release workflow.

## D-024 Version numbers for files and the MSI

- **Status:** Accepted (2026-11-06). Technical consequence of D-018 and D-023.
- **Context:** Releases are `1.2.3` or test versions `1.2.3-alpha.N`,
  `-beta.N`, `-rc.N`. Windows Installer compares only the first three parts of
  a version, so `1.0.0-rc.1` and `1.0.0` would look the same and the final
  release would "repair" the RC instead of upgrading it.
- **Decision:** One rule, in `AppVersion` and nowhere else:
  - File version `Major.Minor.Patch.S` with S = 1000+N (alpha), 2000+N
    (beta), 3000+N (rc), 9000 (stable). Previous-copy names (D-018) never
    collide.
  - MSI version `Major.Minor.(Patch*1000 + T)` with T = N (alpha),
    200+N (beta), 400+N (rc), 999 (stable). Patch is at most 64.
  - The release workflow stamps the tag's version, and the MSI build takes
    its version from the EXE (`version --json`), so they cannot disagree.
- **Consequences:** Programs and Features shows the MSI version (for example
  1.0.999 for 1.0.0). WinTool itself shows 1.0.0.

## D-025 Taking over from WinUpdate Tool also uninstalls it

- **Status:** Accepted (2026-11-07). Owner's request after VM testing. Adds to
  D-015, whose "offers to uninstall the old tool" becomes "uninstalls it".
- **Decision:**
  - WinUpdate Tool V1/V2 counts as present when any of these exist: its
    config.json, its scheduled tasks, an MSI product with its UpgradeCode
    {7BD3183C-C7D9-4F0C-BAE9-FF143A450F53} (V1 and V2 share it), or its
    script install (Uninstall entry "PcNinja WinUpdate Tool").
  - `schedule import-v2` (also behind the window's Take Over) imports its
    settings, removes its tasks, applies WinTool's schedule, then uninstalls
    it: an MSI with `msiexec /x <ProductCode> /qn /norestart`; a script
    install only through the exact QuietUninstallString its installer
    writes, rebuilt by WinTool and never run from the registry.
  - A failed step is reported as a failure ("Incomplete", exit 1), never as
    a completed migration. Portable EXEs and unrelated tasks are not touched.
  - WinTool's MSI runs the takeover after its own installation succeeds (an
    asynchronous commit action that waits for Windows Installer to be free,
    because one MSI cannot remove another while it runs). When the installer
    is given its own schedule, that schedule is kept (`--keep-schedule`);
    otherwise the old tool's schedule is imported.
  - The user is told (owner, after VM round A′): the MSI's schedule page says
    an existing WinUpdate Tool schedule is kept and the tool removed; after a
    background takeover, the next window start shows once what happened and
    the schedule now in use. Each step and the result are in the log.
- **Consequences:** The upstream repositories are not changed. CI covers the
  rules with fakes. VM rounds 2 and A′ (2026-11-07) removed V2 and V1.2 for
  real and imported the V2 schedule.

## D-026 One downloadable EXE that runs portable or installs (prototype first)

- **Status:** Accepted (2026-11-07). The owner approved going ahead after the
  prototype passed antivirus and VM checks (T-011). Amends D-007, D-014 and
  D-018.
- **Decision (owner):**
  - The release page keeps **one main EXE** for people, and keeps the MSI
    beside it for IT and silent deployment. The update feed does not change.
  - The EXE **carries the MSI**, so installing works fully offline.
  - A downloaded copy started without arguments asks: Install or Run
    portable. The command line never asks.
  - **Amended (owner, after T-011):** the question is asked at **every**
    manual launch of the downloaded universal EXE, not remembered. It is asked
    once per launch: the answer travels to the elevated restart as the
    internal argument `--portable-chosen`, which only skips the question. A
    self-update restart and an installed copy never ask.
  - **Added (owner, 2026-11-07):** the question is always asked. When the
    user chooses Install and WinTool is already installed (the MSI's
    `InstallFolder` value) in this version or a newer one, the installed copy
    starts instead of a reinstall. An older installed version is updated by
    the installer. The installed copy itself never asks.
  - **Prototype first:** antivirus risk (D-009, the WinUpdate V2.2.5 lesson)
    is measured before anything replaces today's packaging.
- **Build (acyclic, T-008):** app EXE → MSI → universal EXE carrying that
  MSI. The MSI never contains the universal EXE.
- **Prototype acceptance:** built by CI as a separate artifact; Windows
  Defender scan and VirusTotal results compared with today's EXE; offline
  install, Run portable, and the chooser not shown for CLI use, checked on a
  VM. The owner decides with those results. T-011 (2026-11-07): Defender and
  VirusTotal 0/71 for both the regular and the universal EXE.

---

## D-029 Limited clarification of D-009 for the universal MSI payload

- **Status:** Accepted (2026-11-11), explicit owner approval. Supersedes
  D-009 only to the extent described here; the original entry remains intact.
- **Decision:** D-009 permits only the D-026 universal EXE's embedded WinTool
  MSI to be extracted to a new temporary folder, checked against its embedded
  build-time SHA-256 and opened through Windows Installer in the existing
  approved installation flow. The restriction on dropped executable payloads
  otherwise remains.
- This clarification changes no packaging behavior and grants no merge, tag,
  release or publication authority. Review and owner VM gates remain in force.

## Open questions

None at the moment.
