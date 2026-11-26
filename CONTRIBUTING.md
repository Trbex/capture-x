# Contributing to REAL Capture X

A good capture bug report describes what you selected and what reached the clipboard, file or preview. Include the app version from **Settings → About**, the Windows build from `winver`, and the scaling percentage for each involved display. For recording problems, include the selected sources and the kind of device you used.

Use the [issue forms](https://github.com/Trbex/capture-x/issues/new/choose) for bugs and feature requests. Use synthetic screenshots or recordings when illustrating an issue. For sensitive reports, follow the [security policy](SECURITY.md).

## Local development

Use Windows 11 x64 and the .NET 10 SDK. From the repository root, build Debug and run its self-check.

```powershell
dotnet build RealCaptureX.csproj -c Debug
$exe = '.\bin\Debug\net10.0-windows10.0.26100.0\win-x64\RealCaptureX.exe'
$check = Start-Process $exe -ArgumentList '--self-check' -Wait -PassThru
if ($check.ExitCode -ne 0) { throw "Self-check failed ($($check.ExitCode))" }
```

For interactive QA, run the Debug executable with a separate capture history and preferences. The inspection switch also makes floating windows visible to screenshot and UI automation tools.

```powershell
$env:REAL_CAPTURE_X_INSPECT = '1'
& $exe
```

This profile lives under `%TEMP%\REAL Capture X QA`. To exercise capture exclusion, start a fresh Debug process with `REAL_CAPTURE_X_PROFILE=qa` and the inspection switch unset. Release builds always use the production profile.

## Pull requests

Branch from `main` and describe the user-visible change. Include the build or self-check results and the specific path you exercised. For layout changes, include a before/after pair from synthetic content and identify the theme and display scaling. Record any hardware or display configuration that still needs testing.

The [Windows build](.github/workflows/build.yml) publishes the portable layout, checks its resource index and runtime files, and packages the archive. Maintainers tag the merged commit for a release. See the [README](README.md#build-from-source) for the local publish command.
