# WinTool by PcNinja

One Windows maintenance application that brings together the capabilities of
three PcNinja tools:

- [PcNinja WinUpdate Tool V2](https://github.com/JavierTorresFelendler/PcNinja-WinUpdateTool-V2): Windows Update management and scheduling
- [Windows Upgrade Tool by PcNinja](https://github.com/JavierTorresFelendler/Win-Upgrade-Tool-by-PcNinja): in-place Windows upgrade
- [Smart Office Installer](https://github.com/JavierTorresFelendler/pcninja-smart-office-installer): Office installation and removal

## Status

Released. The latest version is
[1.0.2](https://github.com/JavierTorresFelendler/WinTool-by-PcNinja/releases/latest)
(2026-11-08); 1.0.0 was the first official version (2026-11-07). The three
original tools keep their own repositories and release lines (D-005).

## Download

Get the files from the
[latest release](https://github.com/JavierTorresFelendler/WinTool-by-PcNinja/releases/latest):

- `WinTool-by-PcNinja.exe`: the one EXE for people. At each start it asks
  **Install** or **Run portable**; installing works offline (D-026).
- `WinTool-by-PcNinja.msi`: for IT and silent deployment
  ([MSI specification](docs/MSI-AND-CLI.md)).
- `SHA256SUMS.txt`: the files are not code-signed (D-009), so Windows
  SmartScreen may warn. Check the hashes before running:
  `Get-FileHash .\WinTool-by-PcNinja.exe -Algorithm SHA256`

WinTool updates itself and asks before installing an update. When it finds
WinUpdate Tool V1 or V2, it offers to import the schedule and remove the old
tool (D-015, D-025).

Release notes: [docs/release-notes](docs/release-notes/).

## At a glance

| | |
| --- | --- |
| Platform | Windows 10 1903 or later, Windows 11, x64 |
| Runtime | .NET Framework 4.8 (built into Windows) |
| UI | WPF, follows the Windows dark/light setting; based on the PcNinja Dashboard design |
| Languages | English, Hebrew (right-to-left) and Spanish |
| Packages | One EXE that runs portable or installs (it carries the MSI, D-026), and the MSI for silent deployment |
| License | PcNinja Freeware Use License (free to use; no redistribution or resale) |

## Documents

- [Decision log](docs/DECISIONS.md): what was decided and why, plus open questions
- [Architecture](docs/ARCHITECTURE.md): projects, navigation, CLI, shared rules
- [MSI specification](docs/MSI-AND-CLI.md): setup pages and silent install properties
- [Localization](docs/LOCALIZATION.md): three languages, including right-to-left Hebrew
- [Upstream sync log](docs/UPSTREAM-SYNC.md): how fixes in the existing tools are carried over
- [Command line](docs/CLI.md)
- [Releasing](docs/RELEASING.md)
- [Roadmap](docs/ROADMAP.md): how 1.0 was built

## Requirement carried forward

The application allows only one GUI instance per Windows session. A second
launch brings the running window to the front. This is a safety requirement:
concurrent instances could interfere with operations on a customer's
computer. See [shared/README.md](shared/README.md).

## License

Free to use. The source code is published for inspection only; reuse,
modification, redistribution and resale require written permission. See
[LICENSE](LICENSE).
