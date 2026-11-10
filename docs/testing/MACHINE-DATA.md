# Machine-data validation

Run changing checks only in a designated disposable VM with a snapshot. Record
the candidate commit, EXE/MSI hashes, Windows build, account elevation, command
exit codes and results. Remove machine/account identifiers before sharing logs.
CI checks do not replace the owner's VM acceptance. No release is approved here.

1. Install the candidate MSI. Inspect `%ProgramData%\PcNinja\WinTool`:
   owner is Administrators or SYSTEM; DACL inheritance is disabled;
   SYSTEM and Administrators have full control; Users have read/execute only.
2. Start the application without elevation. Confirm startup logging appears in
   `%LocalAppData%\PcNinja\WinTool\Logs` and the machine ACL is unchanged.
3. On a separate restored snapshot, prepare an existing permissive WinTool data
   folder with nested test data. Start elevated and verify ownership and ACLs
   at the root, nested folders and files. Verify existing valid settings still
   load. Do not substitute real servicing operations for these checks.
4. Repeat non-elevated: an unsafe machine folder must be refused before its
   settings/state are consumed, and logging must use the per-user fallback.
5. Check refusal of a reparse point inside the data tree and an unsafe PcNinja
   parent. The application must not change another product's folder or follow
   the redirected path. Parent-folder problems require administrator review;
   WinTool does not rewrite shared parent permissions.
6. Exercise MSI repair, upgrade and uninstall in the VM; check data-folder
   permissions and the existing schedule after each supported transition.

Local unit tests use only temporary filesystem fixtures. They cover repairing a
permissive nested tree with explicit child grants, refusal without repair,
protected creation, owner/write-grant policy and per-user log fallback. They
cannot establish real SYSTEM/UAC or Windows Installer behavior. Keep locked-file,
ownership-repair and concurrent-access results explicit when VM evidence is
unavailable. ACL repair preserves existing file bytes; it does not authenticate
previously stored data.

`WINTOOL_DATA_ROOT` uses isolated test folders and is exempt from the production
machine ACL policy. Build/version restrictions on selecting that override are
handled separately; this ACL change does not alter its existing availability.
