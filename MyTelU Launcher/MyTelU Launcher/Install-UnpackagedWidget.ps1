param(
    [string]$Configuration = "Debug",
    [string]$Platform = "win-x64"
)

$ErrorActionPreference = "Stop"

# Path to the project root
$projectRoot = $PSScriptRoot
$manifestSource = Join-Path $projectRoot "Package.appxmanifest"

# Detect the actual TFM directory produced by the build (net9.0/net10.0 etc.)
$debugBase = Join-Path $projectRoot "bin\x64\$Configuration"
$tfmDir = Get-ChildItem $debugBase -Directory -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -match '^net\d' } |
           Select-Object -First 1

if ($null -eq $tfmDir) {
    Write-Error "No build output found under $debugBase. Please build the project first (Configuration=$Configuration)."
}

$outputPath = Join-Path $tfmDir.FullName $Platform

if (-not (Test-Path $outputPath)) {
    Write-Error "Output directory not found: $outputPath. Please build the project first."
}

# Define the destination path
$manifestDest = Join-Path $outputPath "AppxManifest.xml"

# Read the manifest content
$manifestContent = Get-Content -Path $manifestSource -Raw

# Replace build-time tokens
$manifestContent = $manifestContent.Replace('$targetnametoken$.exe', 'MyTelU Launcher.exe')
$manifestContent = $manifestContent.Replace('$targetentrypoint$', 'windows.partialTrustApplication')
$manifestContent = $manifestContent.Replace('<Resource Language="x-generate"/>', '<Resource Language="en-us"/>')

# Save the processed manifest to the destination
Write-Host "Writing processed manifest to $manifestDest..."
$manifestContent | Set-Content -Path $manifestDest -Force

# Verify Assets exist
if (-not (Test-Path (Join-Path $outputPath "Assets"))) {
    Write-Warning "Assets folder not found in output. Widgets might be missing icons."
}

# Verify Widget provider exists
if (-not (Test-Path (Join-Path $outputPath "TY4EHelper.Widgets\TY4EHelper.Widgets.exe"))) {
    Write-Error "Widget Provider executable not found in output. Please rebuild the solution."
}

# Register the package in "Sparse" mode
Write-Host "Registering app package..."
Add-AppxPackage -Register $manifestDest -ExternalLocation $outputPath

Write-Host "Success! The widget should now be registered."
Write-Host "You can now run 'MyTelU Launcher.exe' from the output folder or Visual Studio."
