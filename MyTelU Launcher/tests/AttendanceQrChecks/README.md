# Attendance QR Checks

```powershell
dotnet run --project tests/AttendanceQrChecks/AttendanceQrChecks.csproj -c Release
```

Tests use the actual QR service source, synthetic QR images, Windows image
decoding, and an in-memory HTTP handler. They cover official-host validation,
untrusted payloads, expired viewers, response size limits, QR ambiguity, the
submission method/body/authentication, and no-retry behavior after rejection
or an unconfirmed result. No credentials, camera, or live server are used.

The supported contract is the recent opaque MyTelU QR token and the official
`/qrcode/view/` viewer. The older meeting-JSON QR format is intentionally not
mapped to an endpoint without a separately verified protocol.

Pass `-- --fixture S:\path\synthetic-qr.png` to generate a synthetic PNG for
manual UI checks. Never submit this fixture to the real attendance endpoint.
Hardware camera scanning and real attendance recording require separate,
explicitly authorized verification. A server success response alone does not
independently verify the attendance history.

`-- --list-cameras` performs opt-in Windows device enumeration without starting
a stream. Test the native app separately: opening Camera must populate its
selector before Start, the selected device must preview, and stopping, changing
modes, or closing the dialog must release the camera. Verify the Native AOT
publish as well as managed builds: native collection marshalling differs.
