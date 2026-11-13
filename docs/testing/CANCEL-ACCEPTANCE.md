# Focused owner-VM acceptance for D-030

Use the new CI RC artifact only after independent code review, with the pinned
head and SHA-256 recorded. D-030 implements cooperative cancellation; it does
not implement native asynchronous abort. No update test runs on the development
PC. The owner supplies the disposable VM/snapshot. Preserve earlier evidence.

## One download-cancel scenario

1. Close older WinTool instances. Verify the new EXE's hash/version, and record
   the actual running path. Do not change protection settings or exclusions.
2. Scan the VM and select one legitimate applicable update with sufficient
   download time to observe the Downloading stage. If none is available, record
   the missing prerequisite; do not uninstall updates to manufacture a case.
3. Start the manual installation and click Cancel while downloading. Expect
   pending-cancellation text and a disabled Cancel button. Windows may finish
   the current synchronous download. The window remains responsive and waits.
4. Expect a Cancelled result, no new native installation for that pass and a
   message that nothing from this pass was installed. The log must contain
   event 2033 `CancelRequested` with UI stage and UTC time, followed by a
   Cancelled final result. No installation-result marker should appear for this
   run. Event absence alone does not prove no native call; correlate the final
   result and the selected-update state. The exact final-download boundary and
   Installing-report ordering are covered by deterministic fake-agent tests.
5. Collect the relevant run log, state and a screenshot of pending/final state.
   Record head, artifact hash, update identity, request time and outcome. Keep
   identifying raw evidence private. A request that arrived only after native
   installation started is a different case, not a failed download-boundary test.

## If cancellation is requested after installation starts

The button says Stop after this batch. It requests cooperative cancellation of
future work and then disables repeat clicks. Current native installation can
finish. Actual installation results and reboot requirements take precedence;
do not report nothing installed or force-close the process. This observation
may be recorded during the same session if naturally encountered; no extra
installation is required solely to repeat earlier unaffected acceptance.

Reuse earlier B and T-series evidence where unaffected. This focused scenario
does not replace the remaining original A, offline signatures or final release
gates. Production-stamped artifacts require their own authorized smoke test.
