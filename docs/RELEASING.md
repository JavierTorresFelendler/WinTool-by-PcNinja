# Releasing

Build with the .NET 10 SDK selected by `global.json`. Unit-test libraries target
both `net48` and `net10.0-windows`; the shipped application remains x64 .NET
Framework 4.8. Build and run all unit tests before preparing a candidate.

1. Merge the work to `main` and wait for green CI.
2. Tag the commit: `v1.2.3` for a stable release, `v1.2.3-rc.1`,
   `-beta.1` or `-alpha.1` for a test version (D-023, D-024), and push the tag.
3. The **Release** workflow runs the whole CI with that version stamped in.
   Nothing is attached unless every step passed. It then creates a **draft**
   GitHub release with `WinTool-by-PcNinja.exe` (the universal EXE carrying
   the MSI, D-026), `WinTool-by-PcNinja.msi` and
   `SHA256SUMS.txt`; test versions are marked pre-release.
4. Download the draft's files, test them on a VM (checklist in
   `docs/ROADMAP.md`, phase 6), edit the notes (they are shown in WinTool's
   update question), then **Publish**. Only published releases reach users.
5. Self-update uses the public repository (D-017). For the manual local-feed
   update test, build an **rc version**, for example
   `dotnet build WinTool.sln -c Release -p:Version=1.0.3-rc.1` (no tag or
   publication is needed). On the designated VM, set `WINTOOL_UPDATE_FEED` to
   the local releases JSON and `WINTOOL_DATA_ROOT` to an isolated absolute
   folder (`docs/CLI.md`). A stable Release build ignores both variables;
   they work only in Debug or alpha/beta/rc builds. Confirm the reported rc
   version and redirected paths before testing. Restore the VM snapshot
   after changing-operation tests. These variables do not isolate scheduled
   tasks, registry or MSI effects.

CI also builds the Core tests as `0.1.0-rc.1` in a separate artifacts folder
and runs the file-only local-feed lifecycle on both targets. It verifies
download/checksum and portable file replacement using fixture bytes; it never
starts an EXE or installs an MSI. Stable unit tests inject their log directory
directly and do not rely on the test environment variables.

Also with each release: update `OdtSource.FallbackUrl` to the newest Office
Deployment Tool (`office tool-check` shows it; OF-20).
