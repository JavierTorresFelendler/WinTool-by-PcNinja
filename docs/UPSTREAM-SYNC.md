# Upstream Sync Log

WinTool rewrites the code of three tools that are still developed in their own
repositories (decision D-005). This log makes sure that no fix made there is
lost here.

## How to use

1. For each source repository, list the commits after its **Reviewed up to**
   commit:
   `git log --oneline <reviewed-sha>..origin/main`
2. Review each commit and add a row to that repository's table: **ported**
   (with the WinTool commit or PR), **not applicable** (with a reason), or
   **pending**.
3. When every commit up to a given point has been reviewed, move
   **Reviewed up to** to that commit.

A CI job (planned) lists commits newer than **Reviewed up to**.

## WinUpdate Tool V2

- Repository: https://github.com/JavierTorresFelendler/PcNinja-WinUpdateTool-V2
- Baseline: V2.2.8
- Reviewed up to: `323af0af1e5b0f9c9bd62ba05f7b3abd9affe8e8`

| Source commit | Summary | Status | WinTool change / reason |
| --- | --- | --- | --- |

## Windows Upgrade Tool

- Repository: https://github.com/JavierTorresFelendler/Win-Upgrade-Tool-by-PcNinja
- Baseline: 1.3.0
- Reviewed up to: `3f5d87d82f271efb0de9012366aba494b724edb7`

Where the code lives here:

- `src/PcNinja.Win11Upgrade.Core/*.cs` → `src/WinTool.Upgrade/` (same file
  names, namespace `WinTool.Upgrade`). The logic is unchanged; only calls that
  .NET Framework 4.8 lacks were swapped for `Fx`, `Compat` or the shared
  polyfills, and `System.Text.Json` for `WinTool.Core.Json.JsonData`.
- `AppLog`, `AppOperationGate` and `AppUpdateIdentity` → shims in
  `UpgradeLog.cs` over WinTool Core. `AppUpdates/*` is not ported (WinTool has
  its own self-update, phase 6).
- `tests/PcNinja.Win11Upgrade.SmokeTests/*Checks.cs` →
  `tests/WinTool.Upgrade.Tests/Checks/` (one xunit test per group in
  `UpgradeChecksTests.cs`); `Program.cs` → `Checks/EngineChecks.cs`. The
  app-update, single-instance and operation-gate checks are covered by WinTool's
  own tests.
- The WinForms UI is not ported; WinTool has its own WPF screen.

| Source commit | Summary | Status | WinTool change / reason |
| --- | --- | --- | --- |

## Smart Office Installer

- Repository: https://github.com/JavierTorresFelendler/pcninja-smart-office-installer
- Baseline: v3.3.15
- Reviewed up to: `381a08ef89a3392b84d0049fe1c58a4d150c200f`

| Source commit | Summary | Status | WinTool change / reason |
| --- | --- | --- | --- |
