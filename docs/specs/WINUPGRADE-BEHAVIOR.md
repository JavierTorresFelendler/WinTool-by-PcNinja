# Windows Upgrade: Behavior Specification

WinTool's Windows Upgrade area is the Windows Upgrade Tool by PcNinja
(`Win-Upgrade-Tool-by-PcNinja`, stable 1.3.0 at `3f5d87d`) on .NET Framework
4.8. Unlike Windows Update (`WINUPDATE-BEHAVIOR.md`), this engine was already
C#, so its code was **ported, not rewritten**: `src/WinTool.Upgrade` keeps the
original files and logic, and its smoke checks run as `WinTool.Upgrade.Tests`.
This document records the safety rules that must never be weakened, and what
changed around the engine.

Each item is **Keep** (as in the original; a test or the original's own
check proves it), **Change** (reason given) or **Drop** (reason given).

---

## 1. Safety boundaries (never weaken)

| Id | Rule | WinTool |
| --- | --- | --- |
| WUP-01 | Windows Setup is never started, and no computer is upgraded, by tests, CI or a development machine. Tests use fixtures and fakes; "No Setup launched" is part of each check. | **Keep**. CI runs only read-only `upgrade` commands and refusals, never `--yes`. AGENTS.md rule 9. |
| WUP-02 | Windows Server: guidance and an explicit manual command to copy only. Setup is never enabled or run on Server. The Windows 11 client `/product server` switch is not a Server upgrade. | **Keep** (`ManualServerUpgrade`, `ManualUpgradePolicy`). |
| WUP-03 | Never replace an in-place upgrade with a clean or data-only installation. A refused migration stops; it never changes installation type. | **Keep**. |
| WUP-04 | Image selection: same edition, x64 and system UI language, chosen automatically only when exactly one image matches; otherwise an explicit index. No edition or channel conversion. Windows 11 targets must be equal or newer builds; Windows 11 to Windows 10 is blocked; Windows 10 to 10 leaves the build order to Setup. | **Keep** (`InstallationImages`). |
| WUP-05 | Manual x64 Windows 10/11 client LTSC upgrades follow `ManualUpgradePolicy` and exact image edition and language checks. Automatic-download availability never implies manual eligibility. | **Keep**. |
| WUP-06 | Media checks before Setup: system drive space, ISO size, `setup.exe` at the root with a Microsoft signature, image metadata in `install.wim`/`.esd`/first `.swm`, and answer files. A narrowly scoped writable answer file in the selected source is removed; any other answer file blocks Setup. Files on other media and Windows-owned files are never deleted. | **Keep** (`InstallationSafety`, `AuthenticodeVerifier`). |
| WUP-07 | ISO identity: the exact file is mounted and re-validated; a native NTFS proof (file id, USN journal) allows reuse of an earlier validation only when the file provably did not change. | **Keep** (`IsoFileProof`, `IsoValidationSession`). The USN journal parsing runs on the .NET Framework span polyfills; its parser tests run on both runtimes. |
| WUP-08 | Downloads: only when the full system profile (edition, language, architecture, client family, LTSC, build) matches a configured PcNinja source. Resume only the exact original-name partial with matching endpoint, strong ETag, size and range. Stop after 90 s without data. Only a proven-corrupt, recorded PcNinja download is deleted. No "Copy N" files. | **Keep** (`IsoTransfer`, `DownloadSourcePolicy`). |
| WUP-09 | Download pages: own OEM source `https://winoemiso.pcninja.pro/` for matching profiles; otherwise `https://iso.pcninja.pro` and Microsoft's official page. External catalog guidance resolves public landing pages only, on request. | **Keep**. |
| WUP-10 | No forced restart, ever: PcNinja never calls `shutdown.exe`. `readyForRestart` is true only for a real successful preparation that needs a manual restart. | **Keep**. Tested in `UpgradeCliTests`. |
| WUP-11 | Setup arguments: Windows 11 `/product server /auto upgrade /eula accept /dynamicupdate disable /compat ignorewarning /imageindex N /installfrom "<image>"`; Windows 10 the same without `/product server` and `/eula accept`. `/noreboot` and `/quiet` only when asked. | **Keep** (`QuietSetupChecks`, `Windows10TargetChecks`). |
| WUP-12 | Setup outcome after a restart: a correlated rollback is reported with its result and extended codes; missing or stale logs prove nothing either way. | **Keep** (`SetupOutcomeDiagnostics`, `UpgradeHistory`). |

## 2. What changed around the engine

| Id | Original | WinTool |
| --- | --- | --- |
| WUP-20 | Own log file and operation gate. | **Change**: WinTool's log (area `Upgrade`) and WinTool's machine-wide operation gate, so an upgrade and a Windows Update run never overlap. A busy gate is exit 21. |
| WUP-21 | Commands `status`, `downloads`, `scan`, `validate`, `upgrade`. | **Change**: `upgrade status|downloads|scan|validate|run` (`docs/CLI.md`). |
| WUP-22 | Exit codes 0-8. | **Change**: WinTool's codes, 30-33 for upgrade results (D-021). The JSON contract is unchanged. |
| WUP-23 | Without `--yes` the CLI asks "Type YES". | **Change**: the command line never prompts (the window is the interactive path); `upgrade run` without `--yes` or `--dry-run` stops with exit 2 before reading anything. |
| WUP-24 | After a failed download the interactive CLI opens the PcNinja page in a browser. | **Change**: the command line names the page instead; the window still offers to open it. |
| WUP-25 | A repeated option is an error. | **Keep**, now for every WinTool command. |
| WUP-26 | `app-update`, `log-path`, `completion` commands; the application self-update. | **Drop** here. Self-update is WinTool's own (phase 6); the log path is in `upgrade status --json` (`logPath`); PowerShell completion may return later for all of WinTool. |
| WUP-27 | WinForms window. | **Change**: WinTool's WPF Windows Upgrade screen (approved mockup), on the same Core operations as the command line. |
| WUP-28 | `System.Text.Json`. | **Change**: WinTool's `JsonData` reader and `JsonText` writer. The upgrade CLI writes the original property names and enum numbers so existing scripts read the same JSON. |
