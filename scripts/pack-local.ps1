$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$RepoRoot = Split-Path $ScriptDir -Parent
$CounterFile = Join-Path $ScriptDir '.counter'
$OutputDir = '/usr/local/share/nuget/local'

# Read VersionPrefix from Directory.Build.props
$BuildProps = Join-Path $RepoRoot 'Directory.Build.props'
$xml = [xml](Get-Content $BuildProps)
$VersionPrefix = $xml.Project.PropertyGroup.VersionPrefix.Trim()

if (-not $VersionPrefix) {
    Write-Error "Error: Could not read VersionPrefix from Directory.Build.props"
    exit 1
}

# Read or initialize the counter
if (Test-Path $CounterFile) {
    $Counter = [int](Get-Content $CounterFile)
} else {
    $Counter = 1
}

$Version = "$VersionPrefix-local.$Counter"

Write-Host "Packing version: $Version"
Write-Host "Output directory: $OutputDir"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

dotnet pack "$RepoRoot/LayeredCraft.Cdk.Constructs.slnx" `
    /p:Version="$Version" `
    --configuration Release `
    --output "$OutputDir" `
    --no-restore

Write-Host ""
Write-Host "Packed successfully: $Version"
Write-Host "Packages written to: $OutputDir"

# Increment and persist the counter
$Counter++
Set-Content -Path $CounterFile -Value $Counter
