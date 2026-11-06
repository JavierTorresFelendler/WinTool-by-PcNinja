# WinTool by PcNinja

One Windows maintenance application that brings together the capabilities of
three PcNinja tools:

- [PcNinja WinUpdate Tool V2](https://github.com/JavierTorresFelendler/PcNinja-WinUpdateTool-V2): Windows Update management and scheduling
- [Windows Upgrade Tool by PcNinja](https://github.com/JavierTorresFelendler/Win-Upgrade-Tool-by-PcNinja): in-place Windows upgrade
- [Smart Office Installer](https://github.com/JavierTorresFelendler/pcninja-smart-office-installer): Office installation and removal

## Status

Planning. There is no release yet. The existing tools remain independent and
are still maintained in their own repositories.

## At a glance

| | |
| --- | --- |
| Platform | Windows 10 1903 or later, Windows 11, x64 |
| Runtime | .NET Framework 4.8 (built into Windows) |
| UI | WPF, follows the Windows dark/light setting; based on the PcNinja Dashboard design |
| Languages | English and Hebrew; Spanish before 1.0 |
| Packages | MSI (interactive and silent) and portable EXE |
| License | PcNinja Freeware Use License (free to use; no redistribution or resale) |

## Documents

- [Decision log](docs/DECISIONS.md): what was decided and why, plus open questions
- [Architecture](docs/ARCHITECTURE.md): projects, navigation, CLI, shared rules
- [MSI specification](docs/MSI-AND-CLI.md): setup pages and silent install properties
- [Localization](docs/LOCALIZATION.md): three languages, including right-to-left Hebrew
- [Upstream sync log](docs/UPSTREAM-SYNC.md): how fixes in the existing tools are carried over
- [Roadmap](docs/ROADMAP.md)

## Requirement carried forward

The application allows only one GUI instance per Windows session. A second
launch brings the running window to the front. This is a safety requirement:
concurrent instances could interfere with operations on a customer's
computer. See [shared/README.md](shared/README.md).

## License

Free to use. The source code is published for inspection only; reuse,
modification, redistribution and resale require written permission. See
[LICENSE](LICENSE).
