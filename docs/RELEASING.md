# Releasing

Build with the .NET 10 SDK selected by `global.json`. Unit-test libraries target
both `net48` and `net10.0-windows`; the shipped application remains x64 .NET
Framework 4.8. Build and run all unit tests before preparing a candidate.

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

Before step 1, apply D-034: final-head RC CI, independent review and owner VM
acceptance (including Windows 10 and the shipped predecessor update path) must
be recorded. Reuse earlier accepted tests within their limits. Do not merge just
because a preparation PR or CI passed. The owner treats `main` as user-update
readiness and separately authorizes merging and publication; production-file
and public-feed checks remain explicit gates, not RC passes.

The existing **CI** workflow accepts optional manual `candidate_tag`, for example
`v1.0.3-rc.8`, on the candidate branch. That input only stamps validation artifacts;
it creates no tag or release and retains read-only permissions. Manual candidates
accept RC versions only. Normal dispatch without it keeps the development version;
the release workflow's existing `tag` input is unchanged.
See [the short final RC visit](testing/FINAL-RC.md).

1. Only after the above gates and explicit owner authorization, merge the work
   to `main` and wait for green CI.
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
   update test, build an **rc version** with all three version properties set
   consistently as in the local candidate example above (no tag or publication
   is needed). On the designated VM, set `WINTOOL_UPDATE_FEED` to
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
