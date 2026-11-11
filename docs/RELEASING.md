# Releasing

## Local build versions

`Directory.Build.props` deliberately defaults to development version `0.1.0`.
A local build does not infer the latest release from Git tags. Release CI
overrides the informational, file and assembly versions from the supplied tag,
using D-024; it also checks the resulting EXE properties. The MSI version is
derived from the built EXE's `version --json` output.

For a version-stamped local candidate, pass all three values consistently. For
example, `1.0.3-rc.1` uses file version `1.0.3.3001` and assembly version
`1.0.3.0` under D-024. This example is not an assigned next release number:

```powershell
dotnet build WinTool.sln -c Release -p:Version=1.0.3-rc.1 -p:FileVersion=1.0.3.3001 -p:AssemblyVersion=1.0.3.0
```

Build/test success is not release permission. Record owner VM acceptance of the
exact candidate and verify the final release artifacts on the VM before
publication. The steps below describe the separately authorized release flow.

## Release flow

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
