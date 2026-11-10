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
5. Self-update only works while the repository is public (D-017). Before
   that, set `WINTOOL_UPDATE_FEED` to a local copy of the releases JSON to test.
   Add `WINTOOL_DATA_ROOT` (`docs/CLI.md`) so the test does not touch the
   machine's real WinTool data.

Also with each release: update `OdtSource.FallbackUrl` to the newest Office
Deployment Tool (`office tool-check` shows it; OF-20).
