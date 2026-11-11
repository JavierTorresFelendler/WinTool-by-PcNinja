# Machine-data transition and validation

Candidate implementation of D-028, awaiting independent re-review before owner
VM testing. CI does not replace owner acceptance. After review, use a designated
disposable VM and snapshot. Record SHA, artifact hashes, Windows build, elevation
and outcomes; sanitize identifying data.

## Implemented contract

- Machine data lives directly under %ProgramData%\PcNinja-WinTool. The shared
  %ProgramData%\PcNinja parent and other products are unchanged.
- Fresh objects have explicit protected ACLs: Administrators own them,
  Administrators/SYSTEM have full control and Users have read/execute. Safe
  restrictive existing ACLs are retained, including removal of Users read.
  There is no recursive permission repair.
- Unsafe existing directories are renamed to unique .untrusted-GUID siblings and
  replaced with fresh objects. Their contents are not adopted. Reparse points
  are refused. Failure to rename/create is reported, never repaired in place.
  Cached directory handles prevent replacement without scanning descendants.
- Elevated GUI startup, changing CLI commands and the MSI's deferred SYSTEM
  --initialize-data action migrate under the machine operation lock. Read-only
  inspection does not trigger migration. Unavailable settings/state yield
  read-only defaults; saving still fails.
- Migration reads only the old root's WindowsUpdate/settings.json,
  WindowsUpdate/state.json and Upgrade/Last-Upgrade.json. Reads pin ancestors,
  refuse reparse points and multiply linked files, and limit input to 1 MiB.
  Typed validators serialize fresh data; unknown fields, retry claims and legacy
  success evidence are not carried over as trusted proof.
- Invalid source files are reported as not imported; original bytes remain.
  Existing protected destinations are retained. Destination/report/marker write
  failures leave completion unpublished for retry. migration-v1.complete.json
  is written last; migration-v1.report.txt records per-rule dispositions without
  raw input or identifying paths.
- Updates/ and logs are not migrated. Logs page and logs collect read both
  current and old logs. Archive source prefixes preserve same-name logs from
  different roots. Linked logs are refused.
- D-027's task copy is under the new root's Scheduler/ (PR #13). The common
  protection primitive is identical in both independent drafts; consolidate it
  during integration.
- WINTOOL_DATA_ROOT works only in Debug or alpha/beta/rc builds, using PR #14's
  accessor. Its remaining feed policy is an integration dependency. Unit
  logging is injected directly instead of relying on stable-build overrides.

## Evidence and limits

Temporary filesystem fixtures cover replacement with retained old security
handles, read-only refusal, junction/hard-link refusal, once-only migration,
reserialization, failed-write retry and old/current log viewing and collection.
Typed validation tests cover invalid settings and unconfirmed legacy outcomes.
No production root or real task is used locally.

These fixtures do not establish MSI/UAC/SYSTEM transitions, real cross-process
concurrency, antivirus behavior or full migration compatibility. Locked unsafe
folders can block initialization. Quarantined new-root contents are retained
for administrator examination, not imported automatically. Rejected legacy
input is not retried after a successful completion marker; consult the report.

## Owner VM checks after independent review

1. Fresh MSI/portable startup: inspect the new root owner/ACL and completion
   marker. Confirm other PcNinja folders and permissions are unchanged.
2. Upgrade with a legacy user-owned shared parent: allowlisted settings survive
   as newly serialized files; historical success remains unconfirmed. Old logs
   remain visible/collectible, including same-name logs in both roots.
3. With a user-owned new root and retained handles, confirm rename plus fresh
   creation, no payload adoption, and changes through old handles cannot reach
   the new root.
4. Check malformed/oversized/linked input, interrupted migration, destination
   write failure and restart. Confirm no payload/log migration or false completion.
5. With restrictive trusted ACLs and a standard user, check defaults, write
   refusal and per-user logging without permission repair.
6. Exercise install/repair/upgrade/uninstall and concurrent-operation rejection.
   Check D-027's copy location using PR #13's acceptance checklist.

No merge, tag, release or host migration is authorized by this checklist.
