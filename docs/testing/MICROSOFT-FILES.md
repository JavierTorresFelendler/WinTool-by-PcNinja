# Microsoft-file validation evidence

The Core verifier retains WinVerifyTrust Authenticode validation with cache-only
retrieval and no revocation requests. It uses that provider's verified signer
certificate and chain, checks the organization attribute exactly, then applies
the complementary Microsoft-root policy. A policy call succeeds only when its
return value is TRUE and `dwError == 0`. Default production roots are tried first,
then the application-root flag (0x20000); the test-root flag is never used.
WinVerifyTrust state is closed in `finally` after all provider pointers are used.

References: [chain policy](https://learn.microsoft.com/windows/win32/api/wincrypt/nf-wincrypt-certverifycertificatechainpolicy),
[WinVerifyTrust data](https://learn.microsoft.com/windows/win32/api/wintrust/ns-wintrust-wintrust_data),
[verified signer](https://learn.microsoft.com/windows/win32/api/wintrust/ns-wintrust-crypt_provider_sgnr).

## Observed locally, 2026-11-10

- Official [ODT download page](https://www.microsoft.com/en-us/download/details.aspx?id=49117)
  listed `officedeploymenttool_20430-20146.exe`, version 16.0.20430.20146,
  published 2026-10-19. Downloaded from the page's download.microsoft.com link.
- SHA-256: `AF1594121AB610CD8ABC3DD6FF0A504E0CBC6CE54372E5C3813926352D1179C6`.
- The candidate net48 verifier accepted it with policy flags **0**; no
  application-root fallback was needed. Only signature verification ran: the
  ODT executable was not launched or extracted. Logs went to an isolated temp
  folder. This is a wrapper-file check, not extracted ODT setup.exe acceptance.
- The existing Windows curl.exe sample passes on both test targets; a modified
  copy is rejected. Tests also reject unsigned bytes, an untrusted self-signed
  certificate, and TRUE/nonzero-error policy results, and cover exact attribute
  comparison and application-root fallback ordering.

The verifier requests cache-only trust checks, but the development machine's
network was not disconnected. These observations do **not** satisfy the required
disconnected-VM positive-sample gate.

## Required owner VM evidence (not run)

Use the exact candidate commit and record binary hashes. On a disposable VM,
prepare official Windows 10 and Windows 11 media plus the current ODT. Record
each setup.exe's media/build, file version and hash. Disconnect the VM network
before invoking verification. Do not start Windows Setup or an Office install
merely to test a signature. Record accepted/refused and policy flags from the
isolated log, including the extracted ODT setup.exe that the product will run.

Every sample must pass. If both policy variants reject a real sample, stop and
return the file/version/hash and failure codes; do not loosen the trust check.
Add observed root variants to the verifier comment after actual evidence arrives.
CI's existing office tool-check is useful connected-runner evidence, not proof of
disconnected operation or a substitute for both Windows Setup samples.
