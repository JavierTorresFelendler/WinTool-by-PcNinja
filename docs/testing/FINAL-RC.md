# Final RC finishing visit

Do not run until independent review passes the exact source head and the kit
hash is checked. No merge, tag, release or production rollout is authorized.

Use a clean disposable **Windows 10 x64, 1903 or newer** VM with .NET Framework
4.8 and a working rollback snapshot. Record the actual build and snapshot; one
Windows 10 build does not establish every supported Windows 10 configuration.
An existing WinTool installation/data/task or WinUpdate Tool uninstall
registration fails preflight without cleanup. Use a snapshot without V1/V2.
Keep Windows protections enabled. Copy the complete extracted kit to a local
folder such as `C:\WinTool-FinalRC`. Use an elevated administrator PowerShell
5.1 prompt, not the standard-user account. No network connection is needed for
the controlled local-feed upgrade.

```powershell
cd C:\WinTool-FinalRC
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Final-RC.ps1 -ConfirmDisposableVM -ConfirmReviewedCandidate -SnapshotLabel (Read-Host 'Actual restored clean snapshot name')
```

This single visit combines Windows 10 compatibility with both shipped **1.0.2**
update routes. It uses the unchanged published universal EXE and MSI, not the
re-versioned updater driver from the earlier test. The process-only local feed
offers the exact final RC universal EXE/MSI. The old stable 1.0.2 accepts the
feed override under its historical policy; the new RC accepts it under S3.
No override is claimed to work in a new stable build.

1. The shipped portable updater must offer the RC, replace itself with the
   exact universal EXE, and preserve the original `1.0.2.9000` backup bytes.
2. Its GUI opens: choose **Run portable**, inspect normal display, close it,
   then type `YES` in the prompt. Do not click Scan or any servicing action.
3. The helper installs the shipped MSI with scheduling disabled, asks its
   updater for the RC MSI, checks the bytes, and runs that verified MSI.
4. The updated installed GUI opens. Inspect, close, and confirm `YES` again.
   The helper checks the installed plain-EXE hash, uninstalls, and checks no
   WinTool task remains. It waits passively for installer workers, never kills
   them. A reboot request is recorded, not executed or counted as a reboot pass.

Stop on any FAIL/REVIEW; return the unique `Evidence\FinalRC-*.zip` privately.
If ZIP creation fails, preserve its printed raw evidence folder. MSI logs and
CLI JSON remain private (they can contain machine/user paths). Preserve the ZIP
before restoring the snapshot. GUI exit zero without the two owner observations
is not acceptance. Do not repeat a failed run on the modified fixture.

This does **not** rerun Windows Update scan/download/install, cancellation,
migration, SYSTEM scheduling, signature samples, language/theme checks or the
other D-033 campaigns. Prior Win11 RC7 evidence is reused because product source
is unchanged. This kit does not establish the future stable 1.0.3 binary, actual
public-feed delivery, SmartScreen reputation, Defender cloud verdict, or pending
reboot completion. Those remain explicit gates, never implied passes.
