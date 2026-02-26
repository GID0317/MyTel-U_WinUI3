param(
    [string]$Configuration = "Debug",
    [string]$Platform = "win-x64"
)

$ErrorActionPreference = "Stop"

# Path to the project root
$projectRoot = $PSScriptRoot
$manifestSource = Join-Path $projectRoot "Package.appxmanifest"

# Path to the output directory
# Adjust this if your output path structure is different
# Based on your successful dir commands: bin\Debug\net10.0-windows10.0.26100.0\win-x64
$outputPath = Join-Path $projectRoot "bin\$Configuration\net10.0-windows10.0.26100.0\$Platform"

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
