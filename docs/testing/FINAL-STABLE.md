# Stable 1.0.3 validation files, not a published release

Owner authorized stable-stamped files and a short finishing kit only. Exact-head
FABLE review must PASS before executing it. No merge, tag, draft/public release
or rollout. Product source is unchanged from accepted RC8. Do not repeat the
actual predecessor update, Windows Update, migration, scheduling, Cancel,
search/history, signature or language/theme campaigns. Retain D-033 limits.

The CI workflow's separate manual `stable_candidate_tag=v1.0.3` runs the whole
pipeline with Version1.0.3/FileVersion1.0.3.9000/AssemblyVersion1.0.3.0. Manual
RC input remains separate; both at once are rejected. CI is contents:read only;
release.yml and its tag-only publication authority are unchanged. README latest
release remains1.0.2 until separately authorized publication.

## Smoke: one short run on each OS

Use current Windows10 VM first, then a Windows11 rollback snapshot/VM. Preserve
evidence outside the VM before switching OS. Use a clean pre-WinTool snapshot,
x64 client, .NET Framework4.8 and elevated Windows PowerShell5.1. Existing
WinTool/WinUpdate product/data/tasks cause refusal, not automatic cleanup.
Record exact OS build; Win10 21H1/build19043 evidence does not establish22H2.
Extract the whole kit locally to `C:\WinTool-Stable103`.

```powershell
cd C:\WinTool-Stable103
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Final-Stable.ps1 -Phase Smoke -ConfirmDisposableVM -ConfirmReviewedCandidate -SnapshotLabel (Read-Host 'Actual restored clean snapshot name')
```

Choose **Run portable** in the first chooser, inspect the window, close it and
type uppercase `YES`. The helper installs the MSI with scheduling disabled,
waits passively for workers, verifies installed hash and stable runtime, then
opens the installed GUI. Inspect, close, type `YES`; it uninstalls and checks
EXE absent/no own tasks. Both test variables are cleared for all stable steps
and restored in finally. `IsTest` must be false. No scan or other app action.
`Final-RC.ps1` is a hash-pinned source of six previously reviewed functions only;
the stable helper parses their declarations and never runs the RC script body.
Do not launch that RC script separately. Reboot3010 is recorded, never executed.

Return each unique `Evidence\Stable-Smoke-*.zip`. Stop on REVIEW; don't repeat
on a modified fixture. A later official tag build can change hashes: compare
and smoke any changed published-intended files before publication.

## Security: one evidence visit, not once per OS

Keep protections and normal cloud settings enabled; do not add exclusions,
disable protection, remove Internet-zone marking, unblock files or bypass a
block. Use an internet-connected disposable VM. If Defender is unavailable or
definitions are stale, return that condition for review; don't claim a pass.
Check Windows Security's App & browser control shows Check apps and files
enabled; preserve that observation. Missing registry values mean defaults or
unknown, never proof SmartScreen is on. The helper records available settings.

For an actual SmartScreen observation, use Edge **inside the VM** to download
the stable CI run's **WinTool-by-PcNinja-universal-unsigned** artifact ZIP.
Use Explorer's Extract All into Downloads; keep `Zone.Identifier`. This differs
from copying the kit through the shared folder, which may lose Internet marking.
The packet supplies the exact CI run and universal SHA256. No public release is
created to make this download possible. GitHub may require browser sign-in for
Actions artifacts; don't put credentials into logs/scripts. If that route is
unavailable, record SmartScreen NOT_EXERCISED rather than fabricate the marking.

After Smoke, use this command once with the actual extracted downloaded EXE:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Final-Stable.ps1 -Phase Security -ConfirmDisposableVM -ConfirmReviewedCandidate -SnapshotLabel (Read-Host 'Actual rollback snapshot name') -DownloadedExe (Read-Host 'Full path to browser-downloaded, Explorer-extracted universal EXE')
```

It verifies exact bytes and Internet zone, records Defender status/preferences,
requests a custom scan of the kit's final universal EXE/MSI, and collects Defender
events/detections privately. It selects the download in Explorer; double-click
it manually, capture any warning and choose Run portable if the app opens.
Report `NORMAL`, `UNRECOGNIZED`, `BLOCKED` or `UNAVAILABLE`, then close the app.
An unrecognized unsigned-app warning is an observation requiring review, not a
malware conclusion. On a block stop; do not circumvent it. A screenshot of a
warning helps classify it. No new full UI campaign or app-update test.

Security returns **EVIDENCE_COLLECTED_REVIEW_REQUIRED**, never automatic PASS.
Review scan-start/completion correlation and detections in the collected events;
a returned scan cmdlet or empty detections alone is not a completed-scan proof.
Cloud enabled/no observed block does not prove a per-file cloud verdict, and no
positive reputation or browser/public-release URL verdict is invented. Missing
events, offline/error/unknown results stay open. SmartScreen/Defender conditions
need evidence review and owner disposition, not an implied waiver.

Microsoft references: [Start-MpScan](https://learn.microsoft.com/en-us/powershell/module/defender/start-mpscan),
[SmartScreen app reputation](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation).
Raw evidence can contain local paths/security policy details; return ZIPs
privately and sanitize any eventual public summary. The actual public-feed
check stays separate and cannot be claimed from this unpublished preparation.
