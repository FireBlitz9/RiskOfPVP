[CmdletBinding()]
param(
    [string]$Destination = '',
    [string]$Author = 'claytemplar',
    [string]$StagingDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaPvP-package-' + $PID))
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
# Package versions belong to their upstream libraries, independently of Arena's
# own version. Compare every restored runtime package against the build pins.
$buildSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build.ps1') -Raw
$restorePattern = "(?m)^Restore-Zip '[^']+' 'https://thunderstore\.io/package/download/([^/]+)/([^/]+)/([^/]+)/'"
$runtimePins = [regex]::Matches($buildSource, $restorePattern)
if ($runtimePins.Count -lt 6) { throw 'Could not read the build runtime dependency pins; refusing to package unchecked dependencies.' }
foreach ($pin in $runtimePins) {
    $expected = $pin.Groups[1].Value + '-' + $pin.Groups[2].Value + '-' + $pin.Groups[3].Value
    if ($manifest.dependencies -notcontains $expected) { throw "Manifest is missing the build's pinned dependency: $expected. Do not change dependency versions when bumping Arena's version." }
}
if (!$Destination) {
    $version = $manifest.version_number
    $packageName = $manifest.name
    $Destination = Join-Path (Split-Path $PSScriptRoot -Parent) "$Author-$packageName-$version.zip"
}
$dll = Join-Path $PSScriptRoot 'dist\ArenaPvP.dll'
if (!(Test-Path -LiteralPath $dll)) { throw 'Run build.ps1 first.' }
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3)
if ($assemblyVersion -ne $manifest.version_number) { throw "DLL version $assemblyVersion does not match package version $($manifest.version_number). Rebuild before packaging." }
# A fresh payload prevents a reused staging folder from leaking old reports,
# test files or binaries into the playable package. Source artifacts stay separate.
$payloadDirectory = Join-Path $StagingDirectory ('payload-' + [guid]::NewGuid().ToString('N'))
$pluginDirectory = Join-Path $payloadDirectory 'plugins\ArenaPvP'
New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null
$packageFiles = @('manifest.json', 'README.md', 'CHANGELOG.md', 'LICENSE', 'icon.png')
foreach ($name in $packageFiles) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $payloadDirectory $name) -Force
}
# Thunderstore renders the embedded README on its own domain.
$readmePath = Join-Path $payloadDirectory 'README.md'
$figure = "assets/ultra-benchmarks-$($manifest.version_number).png"
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $figure)) {
    $readme = [System.IO.File]::ReadAllText($readmePath)
    $readme = $readme.Replace("]($figure)", "](https://raw.githubusercontent.com/FireBlitz9/RiskOfPVP/main/$figure)")
    $readme = $readme.Replace('](benchmarks/README.md)', '](https://github.com/FireBlitz9/RiskOfPVP/blob/main/benchmarks/README.md)')
    [System.IO.File]::WriteAllText($readmePath, $readme)
}
Copy-Item -LiteralPath $dll -Destination $pluginDirectory -Force
$archiveEntries = @($packageFiles | ForEach-Object { Join-Path $payloadDirectory $_ })
$archiveEntries += Join-Path $payloadDirectory 'plugins'
Compress-Archive -LiteralPath $archiveEntries -DestinationPath $Destination -Force
Write-Host "Packaged $Destination"
