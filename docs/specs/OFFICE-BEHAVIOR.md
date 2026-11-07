# Office: Behavior Specification

WinTool's Office area replaces Smart Office Installer (`pcninja-smart-office-installer`
v3.3.15 at `381a08e`). The original keeps its logic inside one WinForms file
(`OfficeSmart/InstallerForm.cs`, 2,728 lines), so it was **rewritten** as
`src/WinTool.Office`, not ported. Each item is **Keep** (same behavior; a test
proves it), **Change** (reason given) or **Drop** (reason given). Decision
D-022 covers the owner's choices.

Line numbers refer to `InstallerForm.cs` at `381a08e`.

## 1. Products, apps, channels, languages

| Id | Original | WinTool |
| --- | --- | --- |
| OF-01 | Families: Office LTSC Professional Plus 2024 (`ProPlus2024Volume`, channel `PerpetualVL2024`), Microsoft 365 Apps for Enterprise (`O365ProPlusRetail`), and Remove. (`OfficeFamily.cs`, 1977) | **Keep**. Remove is a mode of the screen and `office remove`, not a family. |
| OF-02 | Visio and Project as extra products: `VisioPro2024Volume`/`ProjectPro2024Volume` (LTSC), `VisioProRetail`/`ProjectProRetail` (Microsoft 365). (2019) | **Keep** (D-022: no more products for now). |
| OF-03 | Apps Word, Excel, PowerPoint, Outlook, OneNote (on by default), Access, Publisher, Teams, OneDrive (off by default); unchecked apps are excluded; Lync always excluded. (991, 1413) | **Keep**. **Change**: at least one app must be chosen (the original allowed none). |
| OF-04 | Channels Current (default), Monthly Enterprise, Semi-Annual Enterprise; Microsoft 365 only. (1338) | **Keep**. |
| OF-05 | Languages: English (en-us) and Hebrew (he-il), both on. (1140) | **Change** (D-022): the 44 full language packs from Microsoft's table, with search. The first language chosen is the primary one. **Change** (owner, VM testing 2026-11-07): the default is Windows' display language (another variant of it when Office lacks that one, e.g. fr-BE → fr-fr; else English); on Hebrew Windows, Hebrew then English (`ConfigurationTests`). |
| OF-06 | 64-bit by default, 32-bit option. (1985) | **Keep**. |
| OF-07 | LTSC: activate the volume license during installation (`AUTOACTIVATE`), on by default. (1213) | **Keep**. No keys and no activation tools are included. |
| OF-08 | Remove previous Office before installing (`<Remove All="True" />`), off by default. (1221) | **Keep**. On the command line it needs `--confirm`. |

## 2. Configuration file

| Id | Original | WinTool |
| --- | --- | --- |
| OF-10 | The XML layout of `BuildXML` (1982): Add with edition and channel, products with languages and exclusions, optional Remove, Display Full with AcceptEULA, AUTOACTIVATE for LTSC, RemoveMSI. | **Keep**, byte for byte for the same choices (`ConfigurationTests`). |
| OF-11 | SourcePath only for the offline download, XML-escaped. (1987) | **Keep**. |
| OF-12 | Always `Display Level="Full"`. | **Change**: `Display Level="None"` with `--quiet` on the command line, and with **Silent installation** in the window (off by default; install and package, owner request 2026-11-07). WinTool still shows progress and the result. Remove stays Full in the window. |
| OF-13 | Visio and Project get the same languages as Office. | **Change**: en-GB, fr-CA and es-MX are skipped for Visio and Project, which Microsoft does not offer in them; English is used when nothing is left. |
| OF-14 | Remove-all file: Remove All, Display Full, AcceptEULA. (1819) | **Keep**. |
| OF-15 | XML written with a UTF-8 byte order mark. | **Change**: without it. The ODT reads both. |
| OF-16 | Configuration files in `%TEMP%` under fixed names (`pcninja_cfg.xml`, `pcninja_odt.exe`, `pcninja_odt_x`). (64) | **Change**: a private folder per run (`%TEMP%\WinTool-Office-<id>`), deleted after success and failure. Fixed names let two runs, or another program, interfere. |

## 3. The Office Deployment Tool

| Id | Original | WinTool |
| --- | --- | --- |
| OF-20 | Downloads a fixed ODT build (`officedeploymenttool_18827-20140.exe`) with WebClient. (1792) | **Change** (D-022): the newest `officedeploymenttool_*.exe` link on Microsoft's download page (id 49117), else the fixed address in `OdtSource.FallbackUrl` (now 20430-20146, October 2026), updated with each WinTool release. `office tool-check` on CI proves the page lookup and the signature on every build. |
| OF-21 | Runs the download without checking it. | **Change**: the downloaded ODT and the extracted `setup.exe` must have a valid Microsoft Corporation signature before they run (`DeploymentTests`). Exit code 41 otherwise. |
| OF-22 | Extracts with `/quiet /extract:`; a non-zero exit is a failure. (1800) | **Keep**. Also a failure when `setup.exe` is missing afterwards. |

## 4. Install, package, remove

| Id | Original | WinTool |
| --- | --- | --- |
| OF-30 | Install: `setup.exe /configure` and wait; non-zero exit is a failure. (1840) | **Keep**. The exit code is reported (decimal and hex) and is the CLI's `setupExitCode`. |
| OF-31 | Remove: the remove-all file with `/configure`. (1815) | **Keep**. The window asks for confirmation; the CLI needs `--confirm`. |
| OF-32 | Package: chooses a folder, uses it or `<folder>\OFFICE-OFFLINE`. (1966) | **Keep**. |
| OF-33 | Package: downloads into a staging folder, then copies the contents of `Office` into the package root, so the package holds `Data\` beside `setup.exe`. (1867-1929) | **Change**: downloads straight into the package (`SourcePath` = package), which gives `Office\Data\` beside `setup.exe`. That is the layout Microsoft documents for installing without a SourcePath; with `Data\` at the root, the ODT likely falls back to the internet. To confirm on a test machine. |
| OF-34 | Package files: `setup.exe`, `Office_Config.xml` (no SourcePath), `Install-Office.bat` (checks for administrator, runs `setup.exe /configure Office_Config.xml`). | **Change** (T-001): keep the layout, names and administrator check; the ASCII launcher now captures the setup exit code immediately, reports success only for 0 or failure with the numeric code, and returns that same code after the pause. This replaces the unconditional completion message (`InstallScriptTests`, stub exits 0 and 42). |
| OF-35 | Download progress from the size of the Office folder every second, a line at least every 10 s, 52-88 %. (1934) | **Keep**. MB/min is dropped from the line. |
| OF-36 | Cleans temporary files only after success. | **Change**: always (OF-16). The ODT's own logs stay in `%TEMP%`. |
| OF-40 | No protection against another tool running at the same time. | **Change**: under WinTool's operation gate (exit 21 / message when busy). |
| OF-41 | No cancel. | **Change**: cancel works until the Office installer starts, and during a package download (which is safe to stop). The installer is never interrupted. |
| OF-42 | Progress and log in the window only. | **Change**: also WinTool's log (area `Office`). |

## 5. Window and command line

| Id | Original | WinTool |
| --- | --- | --- |
| OF-50 | A step-by-step wizard (family, products or channel, languages, options, install). | **Change**: one screen (approved mockup) with the same choices and a summary, including the Office installed now. |
| OF-51 | No command line. | **Change** (D-022): `office status|config|install|package|remove` (`docs/CLI.md`). |
| OF-52 | Its own update check against its GitHub repository. | **Drop**: WinTool updates itself (phase 6). |
| OF-53 | Own dark purple design. | **Drop**: WinTool's themes. |
