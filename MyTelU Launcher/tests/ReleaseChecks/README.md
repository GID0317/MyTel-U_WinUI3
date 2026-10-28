# Release Checks

Windows-only checks using the production storage and image-decoding sources.
Run from the solution directory with the SDK selected by `global.json`:

```powershell
dotnet run --project tests/ReleaseChecks/ReleaseChecks.csproj -c Release
```

The checks create and remove their own uniquely named temporary directory.
They do not read or modify your account, credentials, real caches, or tools,
and they make no network requests. Set `TEMP` to another drive before running
when the system drive is low on space.

Coverage includes encrypted round trips, bit flips, truncation, random
replacement, plaintext rejection, wrong DPAPI entropy, oversized files,
forged tool settings, malformed settings recovery, bounded image decoding,
and session-request queue cancellation and release.

These are regression checks, not a complete penetration test. DPAPI protects
data under the Windows account: code running as that account can decrypt or
replace it. Replaying an older valid encrypted cache is not prevented.
