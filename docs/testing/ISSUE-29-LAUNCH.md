# Issue #29: bounded universal launch acceptance

Implementation: D-039, based on the reviewed plan at 949d85d. This is a proposed
single-visit checklist for the exact code-review head, not an acceptance result.
Run only after independent code PASS, on a disposable Windows VM with a clean
snapshot and a real standard account. No merge, tag or release authority.

## Scope and evidence

Use one candidate's universal EXE and matching MSI, with exact CI run, commit,
runtime version and SHA-256 manifest pinned in its kit. Keep the shipped 1.0.3
MSI as the older install fixture. A proposed candidate label is 1.0.4-rc.1; this
does not choose a future production release or change published 1.0.3 files.
Do not run Windows Update scans/installs, Office, Windows Setup or scheduling.
Keep protections enabled. Close all WinTool windows between cases unless the
case explicitly tests activation. Preserve logs privately before rollback.

Existing whole-product RC8/stable evidence remains valid within its unchanged
scope. Do not repeat theme/page/servicing/update-feed campaigns here.

## One visit, two accounts

1. **Clean / administrator:** launch the universal normally and Cancel the
   existing Install / Portable question. Then use lone `--portable`; expect no
   question and a normal window. Close it. Mixed `--portable --json` and
   `--portable-chosen --json` must exit 2 with no window or installer.
2. **Older protected installation / administrator:** install shipped 1.0.3 MSI
   with scheduling disabled and wait for its background workers to finish.
   Launch the newer universal. Expect product versions in Update X to Y.
   Choose No: the installed 1.0.3 window opens, no installer. Close it. Launch
   again, choose Yes, approve the one explicit update. Capture the MSI result.
3. **Same-version installed / administrator, then standard user:** launch the
   downloaded universal from an already-elevated prompt, then from Explorer in
   the real standard account (close between launches). Expect the registered
   protected installed EXE, no chooser or MSI, and no self-collision. The usual
   UAC prompt is allowed. Cancel UAC once: no alternative installer/portable
   fallback. With the installed window open, another launch activates it.
4. **Isolated startup fixtures / administrator:** after preserving the above
   evidence, use rollback-isolated registered metadata fixtures, never change
   production files. A protected fixture with a higher version blocks both
   portable markers and self-update restart intent before migration/maintenance.
   The default launch still redirects to a protected newer installation. A
   writable same-version fixture is never launched: only Portable / Cancel;
   older writable fixture allows Update / Portable / Cancel; newer writable
   fixture blocks. Confirm a standard account really can write the unsafe
   fixture, and cannot write the protected fixture. No ACL repair/relocation.
5. **Failure boundary / administrator:** a protected, consistently versioned
   fixture that cannot be executed must show path/reason and exit 6. No MSI or
   portable window may open. A changed target/protection after a dialog must
   similarly stop redirection. Restore the snapshot after exporting evidence.

The fixture preparation must be pinned and reviewed before execution; this
checklist alone is not a fixture script. No owner is asked to hand-edit registry
or ACLs from this document. Stop and return evidence on unexpected mutation.

## Automated versus VM claims

Core tests cover the pure decision/answer/argument table, file/product version
consistency, both markers, changed facts after elevation and install eligibility.
Tests do not execute Program, WPF, UAC, registry discovery or Process.Start.
The app executor therefore needs exact-head source review plus the visit above;
unit green alone is not startup acceptance. Builds without embedded MSI retain
their argument/startup behavior and remain covered by the existing CI self-tests.
The trust check is a path/ACL observation immediately before Process.Start, not
an atomic executable-handle launch; administrator races are not eliminated.
