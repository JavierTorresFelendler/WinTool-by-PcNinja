# Issue #29 launch kit: review before VM execution

Product code PASS: FABLE, `16e663a`, tree `f5bd63f3`. Product artifacts are the
unchanged `1.0.4-rc.1` files from CI run 43761429507. Helper approval is separate:
do not run this kit until FABLE passes its exact helper head and manifest.
No merge, tag or release. Use one disposable Windows client VM, clean snapshot,
and an existing enabled real standard local account. Keep protections enabled.

Extract to **C:\WinTool-Issue29**. Do not overwrite an earlier kit or failed run.
Preserve evidence before rollback. Do not scan/install Windows updates, run
Windows Setup/Office, enable scheduling, or start tasks. Only the fixture's
explicit application MSI install/update/uninstall is intended.

## Three commands, one visit

1. **Administrator, elevated Windows PowerShell 5.1**, after clean rollback:

   ```powershell
   cd C:\WinTool-Issue29
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Issue29-Launch.ps1 -Phase Prepare -ConfirmDisposableVM -ConfirmReviewedKit -SnapshotLabel (Read-Host 'Actual clean snapshot name') -StandardUserName (Read-Host 'Existing standard local account name')
   ```

   Follow each short prompt. Clean chooser: Cancel; deliberate Portable: close
   without operations. On the older-install question choose No first, then Yes
   on the next launch. Finish that application installer with scheduling **off**.
   For a same-version redirect, leave the installed window open when asked,
   confirm activation, then close it. Stop unless the helper reports
   `PREPARE_PASS_STANDARD_USER_PENDING`.

2. **Sign into the named standard account**, ordinary PowerShell:

   ```powershell
   cd C:\WinTool-Issue29
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Issue29-Launch.ps1 -Phase StandardUser -ConfirmDisposableVM -ConfirmReviewedKit
   ```

   For the first redirect approve the normal UAC prompt with the VM administrator
   credentials; observe the installed window and activation, then close it.
   On the separate cancellation case decline UAC. No alternative window or
   installer should open. The helper opens fixture EXEs for ReadWrite access
   without writing executable contents: unsafe succeeds, protected is denied.
   Continue only after `STANDARD_USER_PASS`.

3. **Return to administrator**, elevated PowerShell:

   ```powershell
   cd C:\WinTool-Issue29
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Issue29-Launch.ps1 -Phase Finish -ConfirmDisposableVM -ConfirmReviewedKit
   ```

   On unsafe-choice dialogs choose **Cancel**, and dismiss blocked/failure
   messages. The changed-trust case is two prompts: leave Update open; press
   YES in PowerShell; then choose No in the application and dismiss its error.
   No fixture installer/Portable selection is intended. Final expected result:
   `SCOPED_LAUNCH_PASS`, after candidate uninstall and zero own tasks.
   Return the Prepare and Finish ZIPs privately; Finish includes the standard
   account's raw evidence. Export them before restoring the snapshot.

## Fixture containment and recovery

- Test stubs have conspicuous TEST FIXTURE assembly metadata and only write
  `executed.marker` next to themselves; they do not run WinTool operations.
  The three versions are 1.0.3, 1.0.4-rc.1 and 1.0.5. Compilation and kit
  creation never invoke their entry points.
- A fresh GUID directory directly under Program Files holds the session and
  stubs. Administrators/SYSTEM own it; Users read it. Only unsafe subfolders
  grant Users Modify. The standard account can write only its evidence folder
  and the intentionally unsafe stubs. The helper never repairs existing paths.
  Admin collection parses the standard account's evidence through the existing
  Core plain-file reader (no junction/reparse/hard-link traversal), with bounded
  directory/file counts and the reader's 1 MiB per-file limit.
- The failure stub denies Everyone ExecuteFile while keeping read access;
  the unchanged product ACL verifier must still classify it as protected.
  A process-start failure is then expected, with GUI exit 6 and no fallback.
  This is a fixture precondition, not a weakened product protection check.
- Registry writes touch only the existing 64-bit `InstallFolder` value. Its
  original value/type are stored in the protected session. Each case restores
  them in finally, plus an outer recovery finally. Short Core operation leases
  cover fixture/registry changes; **no lease is held during product launches**.
- If PowerShell is interrupted, do not launch WinTool. Restore the snapshot,
  or first run the reviewed helper as administrator with `-Phase Recover` and
  both confirmation switches. It only restores a recognised fixture override
  to the saved original value. It does not resume tests or report a PASS.
- No processes are killed, tasks started, protection disabled, directories
  recursively deleted or foreign registry changes overwritten. Lab folders
  remain for evidence and disappear on owner rollback. Stop on every REVIEW.

## Evidence limits and unchanged code notes

The standard-user GUI/UAC observations are owner attestations: the account may
not read an elevated process's executable path. Admin capture checks that path;
the standard-user token/SID, write probes and per-user product logs are saved.
The fake newer target proves routing and markers, not a real newer GUI. It is
not a servicing, feed, MSI custom-path or multi-OS campaign. Prior accepted
whole-product evidence is reused; do not repeat it for this visit.

FABLE's four low code notes remain deferred: the unsafe-newer block's wording,
silent mixed-flag usage, unexpected legacy metadata falling back to the old
choice, and unused legacy `InstalledCopy.Decide`. Product files stay at the
passed head. In particular, the known block wording does not permit opening an
unprotected installed copy.

## Reproducible kit creation (build PC, no fixture execution)

Use 64-bit Windows PowerShell 5.1 with `tools/vm/Build-Issue29Kit.ps1` and explicit
`-CandidateRoot` (the downloaded RC directory with Universal/Plain/MSI children),
`-BaselineMsi` (published 1.0.3), and fresh `-OutputDirectory`. It hash-checks the
inputs, extracts Core reflection-only, compiles harmless stubs, copies the
reviewed helper/readme and writes metadata plus SHA256SUMS. Never run the VM
helper, candidate or MSI on the build PC. Kit ZIP/hash belong in the review packet.
