# Selected-update search optimization: focused acceptance

Issue #22 / D-031, separate from the reviewed RC3 cancellation patch. Test only
the exact independently reviewed new head and artifact hash on an owner-designated
disposable VM with rollback. No development-machine product scan/install.

## Source and unit evidence

Initial discovery remains three queries. Selected revalidation uses primary
retained-object sources, fresh objects and frozen identity/revision/scope.
Unknown/mismatched context or in-batch instability completes full validation,
reusing already completed classes. Three validation queries maximum. Required
failure aborts, without retry; changed revision skips/reports without replacement.

Successful GUI operation without pending restart: initial3 + validation1..3 +
engine post-snapshot3 =7..9 searches, versus RC3's10. Known reboot outcome:
initial3 + validation1..3 + Broad-only1 =5..7, versus7. All unavailable:
initial3 + validation1..3 =4..6; no install or post-scan, prompt user to Scan again.
Native latency is not guaranteed by count reduction. Logs include per-query UTC,
monotonic ticks/frequency, context and elapsed time INCLUDING metadata work.

Event IDs2025/2026 are detailed query begin/end records;2027 is validation plan
or fallback;2028 is WU26 verification referencing the shared query ID. These
information diagnostics go to product logs; the Application milestone allow-list
is unchanged. A failed shared Broad is Verification Unknown and partial UI;
native install results and reboot requirements remain independent.

## One focused owner-VM run (after exact-head code review)

1. Preserve older evidence. Use the new plain portable candidate in a separate
   folder; record its hash/version and the owner-reported rollback snapshot.
2. Ensure pending servicing/restart is complete; keep protections and policies.
   Explicitly click Scan: no automatic launch scan is introduced. Select one
   applicable ordinary Windows update whose primary source is Broad.
3. Install that selection once. Do not click Cancel for this performance case.
   Capture revalidation plan/fallback, query IDs/context/counts/timing and final
   outcome. In a stable known context, validation should use one Broad; with
   unknown/changed context, exactly three classes and an explicit fallback reason.
4. On success without restart, verify one shared post-snapshot3 and no automatic
   GUI Scan afterwards. If reboot is required, verify Broad-only post-query and
   the restart message; do not perform a second install merely to get7 calls.
5. Return the private evidence ZIP and final-state screenshot. Stop if Windows
   is busy or no legitimate update is available; record the prerequisite rather
   than reset services/uninstall packages solely for testing.

Compare query/stage counts and measured intervals with the disclosed RC3 baseline:
pre-install wall brackets22/13/15s and145/21/27s, NOT controlled native timings.
Original RC3 lacked query-start timestamps. VM Suspend/clock corrections can
affect wall intervals; record those conditions rather than claim a speedup.

## Race and coverage limits

The report-only busy probe is acquire/release BEFORE search, advisory only. A
scheduled collision at that instant returns AlreadyRunning/21; WinTool does not
request retry or save a new outcome state on that path. Otherwise scheduled runs
can acquire after release while GUI searching; deterministic fake tests cover it.
No extra race VM run is requested. Getter context is requested/reported, not
network endpoint verification, and does not detect every WSUS/policy change.

No repeated A/B/full T-series requested. Separate T24/T25, production-stamp,
real-feed update, SmartScreen/cloud and owner decision gates remain open. No
merge, tag, release or production rollout is authorized by this acceptance file.
