# Integrated maintenance candidate

Owner-authorized integration of eight independently reviewed drafts on a separate
branch. Main and the source PRs remain unchanged. This is a candidate for final
independent review, then owner VM acceptance; it is not shipping approval.

| PR | Reviewed head |
| --- | --- |
| #12 S1 | 6ffa9e2046de17f4c3e96415d5bb5df5d99e0a9b |
| #13 S2 | 93761fd579aaffaa0516aa04d7585d2fd395543a |
| #14 S3 | 9f801f27db00581c7857c49e9fa3c76bb8264e79 |
| #15 S4 | 40f94cd31da38ac5a19acf601d991799863528fe |
| #16 SDK | 8c7224addde66b368be04ee297c74e5cf9969da9 |
| #17 lock | a9dca16ada7c725f0f5d5e709055b057ece8145b |
| #18 CLI tests | c71eb5b32a42c1c05d6c1a468def4a53bd101b3a |
| #19 docs | 59516f7eb19fe8b04eb72c4d2f6b153d8b36738b |

## Integration choices

- Core has one unchanged protected-data primitive from S1/S2. S3's effective
  data-root and feed gates remain at every accessor and elevation check.
- GUI data initialization precedes background worker refresh. Changing CLI
  initialization precedes the selected maintenance policy; read-only commands
  do not trigger migration or worker refresh. Busy and protected-data failures
  retain their separate CLI outcomes.
- TaskMaintenance uses S2's superset. MSI sequence: registry, data initialization,
  optional explicit schedule application, optional best-effort worker refresh.
  Uninstall uses S2's guarded removal path. No new automatic servicing operation.
- S4's complete verifier is retained over the SDK migration hunk. S1/S3/S2
  overlaps in AppUpdater retain both protected output and gated feed selection.
- All five test projects target net48/net10.0-windows. New CLI cases followed
  the extraction, including AppUpdateWorkerRefreshTests. Shared test fakes and
  stable-build log injection remain in place. The shipped App is still net48.
- D-027/D-028/D-029 appear in order, with original D-009 unchanged. Architecture
  and releasing documentation describe the resulting SDK/test layout.

## Test artifact identity

Ordinary CI validates the stable development configuration. The additional
Integrated RC candidate workflow calls only CI with version v1.0.3-rc.1, using
the existing version example and stamping rule. This test identity permits the
1.0.2-to-candidate MSI upgrade and local-feed rc tests on the disposable VM.
It is not a release/version-publication decision: no Git tag or GitHub release
is created. The Release workflow is never invoked. The caller is restricted to
the integration branch's PR and has contents-read permission.

The exact head, CI run IDs, downloaded artifact hashes and private review packet
are recorded separately after validation. Owner VM tests must use those exact
RC artifacts after the integrated-head review, not the individual PR builds.

## Acceptance still open

Keep the previously recorded S1 deferred-migration/save and held-handle limits,
S2 best-effort refresh/removal and bounded wait behavior, nontransactional task
registration, actual self-update child behavior and mapped-source limitations
explicit. Owner checks include legacy upgrade, task ownership, malformed data,
installer robustness, concurrency, Defender provisioning flow and real offline
Microsoft Setup/ODT samples. CI cannot replace these acceptance cases.

No main merge, VM execution, tag, release or publication is authorized here.
