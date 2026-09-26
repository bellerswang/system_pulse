param([switch]$SkipStartup)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $repoRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$project = Join-Path $repoRoot 'src\XinweiManager\XinweiManager.csproj'
$publishDir = Join-Path $repoRoot 'artifacts\publish'
$target = Join-Path $repoRoot 'app'
$targetFull = [IO.Path]::GetFullPath($target)
$expectedParent = [IO.Path]::GetFullPath($repoRoot)
if (-not [string]::Equals([IO.Path]::GetDirectoryName($targetFull), $expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals([IO.Path]::GetFileName($targetFull), 'app', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected publish target path.'
}
foreach ($checkPath in @($expectedParent, $targetFull)) {
    if ((Test-Path -LiteralPath $checkPath) -and ((Get-Item -LiteralPath $checkPath).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Publish target cannot be a link or junction.'
    }
}
$env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget\packages'
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet_home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (Get-Process -Name XinweiManager,SystemPulse -ErrorAction SilentlyContinue) {
    throw 'Exit System Pulse from the tray before publishing a new version.'
}
& $dotnet restore $project -r win-x64 --configfile (Join-Path $repoRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o $publishDir --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
New-Item -ItemType Directory -Force -Path $targetFull | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $targetFull -Recurse -Force
foreach ($oldFile in @('XinweiManager.exe', 'XinweiManager.dll', 'XinweiManager.deps.json',
                       'XinweiManager.runtimeconfig.json', 'XinweiManager.pdb')) {
    Remove-Item -LiteralPath (Join-Path $targetFull $oldFile) -Force -ErrorAction SilentlyContinue
}
$exe = Join-Path $targetFull 'SystemPulse.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Published executable is missing.' }
if (-not $SkipStartup) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -Path $runKey -Name 'SystemPulse' -Value ('"' + $exe + '" --background') -PropertyType String -Force | Out-Null
} else {
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SystemPulse' -ErrorAction SilentlyContinue
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'XinweiManager' -ErrorAction SilentlyContinue
$legacyParent = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'XinweiManager'))
$legacyTarget = [IO.Path]::GetFullPath((Join-Path $legacyParent 'app'))
if ([string]::Equals([IO.Path]::GetDirectoryName($legacyTarget), $legacyParent, [StringComparison]::OrdinalIgnoreCase) -and
    [string]::Equals([IO.Path]::GetFileName($legacyTarget), 'app', [StringComparison]::OrdinalIgnoreCase) -and
    (Test-Path -LiteralPath $legacyTarget) -and
    -not ((Get-Item -LiteralPath $legacyParent).Attributes -band [IO.FileAttributes]::ReparsePoint) -and
    -not ((Get-Item -LiteralPath $legacyTarget).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    Remove-Item -LiteralPath $legacyTarget -Recurse -Force
    Write-Output "Removed previous copy: $legacyTarget"
}
Write-Output "Published: $exe"
Write-Output "Launch at sign-in: $(-not $SkipStartup)"
