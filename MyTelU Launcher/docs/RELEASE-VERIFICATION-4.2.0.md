# 4.2.0 Release Verification

Checked on Windows x64 on 2026-10-28 with .NET SDK 10.0.403.

## Improvements

- Decode accent-analysis images to a maximum 256-pixel edge instead of decoding
  and copying a full wallpaper. Bound title-bar and About icon decoding to 64 pixels.
- Ship only the Windows Playwright driver, including when older build folders
  still contain macOS/Linux drivers. The installed Edge driver smoke test passed.
- Remove the widget's plaintext cache/cookie fallback. Widget settings now use
  the same encrypted format as the launcher; old plaintext data is not imported.
- Reject encrypted files and local settings larger than 16 MiB before allocating
  their contents. Malformed local settings recover to defaults instead of crashing.
- Coordinate iGracias page-and-AJAX requests across launcher/widget processes.
  A controlled test reproduced the failure: visiting Attendance between the
  Grades page request and its AJAX request returned non-JSON instead of grades.
- Include the Session Detail Native AOT interop fix, profile-picture hiding,
  and removal of duplicate sign-out commands from academic-page menus.

## Measurements

These are local observations, not universal memory guarantees. Measurements used
one standalone Release Native AOT launcher, the same Home page, background,
window size, and saved settings. No forced garbage collection or working-set
trimming was used.

| Measurement | Before | After |
| --- | ---: | ---: |
| Home private memory, eight-sample mean | 237.8 MiB | 156.1 MiB |
| Home working set, eight-sample mean | 255.6 MiB | 178.9 MiB |
| Complete standalone runtime | 1,071.5 MiB | 596.1 MiB |
| Authentication helper payload | 682.0 MiB | 206.6 MiB |

The final unsigned Release MSIX is 241.1 MiB. A previous Debug MSIX is not an
equivalent size baseline and is deliberately not used for a percentage claim.

## Verification

- 62 offline release checks passed in both the development folder and Git checkout.
  See `tests/ReleaseChecks` for the reproducible storage, tampering, oversized-file,
  protected-tool-setting, image-decoding, and session-queue regression checks.
- 28 model/cache/live academic checks passed, including official profile, TAK,
  and KSM requests plus three concurrent Schedule/Grades/Attendance batches.
  Every batch returned data for all three pages. Personal responses are not committed.
- The final standalone Native AOT app opened Session Detail and rendered five
  sessions with Hadir and Sakit status badges without crashing.
- The same app loaded Grades and TAK, then opened Score Detail and rendered
  component scores and their weighted total without crashing.
- Live account-menu inspection verified generic person icons without photos or
  initials and KTM Digital above Sign out. XAML checks verified Hide profile
  picture above Community Tools and separators before More information in all
  three academic-page menus, with no duplicate sign-out commands.
- NuGet vulnerability checks, including transitive dependencies, reported no
  known vulnerable packages for the launcher, Core, ColorThief, authentication
  host, or widget at verification time.
- Release MSIX build succeeded. Payload inspection verified the native launcher
  matches the published binary, has no CLR header, and includes both helper
  executables. No private keys, user caches, or non-Windows Node binaries were found.
- Existing nullable, experimental TableView, PRI qualifier, and optional-symbol
  tooling warnings remain; this is not a warning-free build.

## Limits and Release Gate

- Certificate files, signing configuration, publisher identity, and icon assets
  were not changed. The QA MSIX is unsigned: signing, installation/update, and
  activation from the installed Widgets Board still need checking with the
  release certificate before distributing an installer.
- Only x64 was tested. ARM64 was not validated.
- These checks are not a comprehensive penetration test or proof of immunity to
  all attacks. DPAPI protects data in the Windows-account context, not against
  malware already running as that user or an administrator. Replaying an older
  valid encrypted cache is not prevented, and cached academic records are not
  authoritative university-signed records. Harmless appearance settings remain
  editable; saved tool destinations use protected settings.
- The authentication host and widget remain managed/self-contained; only the
  main launcher uses Native AOT. Keep the existing experimental Windows App SDK
  requirement for the schedule TableView.

QA MSIX SHA-256:

```text
C28D2809A1C140CE0CEC583453C9781C3897D233A6ABCD34CF4A93C80EB763D8
```
