# Last recorded run: dashboard and Windows Update (issue #24)

Separate UI-only change after pinned issue #22. Both pages show the existing
saved WindowsUpdateState timestamp, localized run type and localized outcome,
under Last recorded run. It is historical data, not a new scan/current status.
No new saved format, log parser, logging or engine behavior is required.

Read missing/null/unreadable/corrupt state as No recorded run available, with no
error dialog. Validate timestamp/type/result before presentation; no raw state
message or unrecognized enum text is displayed. A saved Running record uses its
start time and explicitly says completion unknown; never pair it with the old
finish timestamp of a previous run. Local ISO times retain saved local meaning.

The existing dashboard state reads are reused; Windows Update reads saved state
when its normal summary refreshes. No new native Search, registration, history
query, provider/service/operation lock or scan is added on activation. The engine,
updater, protected data, scheduling and state/log format must have identical
Git blobs to issue #22 head2293c91 (including M1). UI helper tests source-link the WPF-free
presentation class, avoiding an app-reference/architecture refactor.

## Acceptance

- Unit-test valid, absent, unreadable/corrupt and contradictory records; all known
  run types/outcomes have en/he/es strings; do not display raw LastMessage.
- CI renders existing page/theme/language smoke coverage. Treat this as separate
  exact-head code review; do not reopen issue #22's engine scope.
- After BOTH PRs pass independent review, the UI child candidate can carry the
  exact unchanged issue #22 engine into its ONE focused servicing VM check. View
  dashboard and Windows Update before clicking Scan: prior saved status or empty
  line should appear with no new discovery. Observe updated saved result after
  the one approved operation; no second installation solely for this UI change.
- Check the short labels in three languages during that same visit if available;
  do not repeat all A/B/T-series. No merge, tag or release authorization.
