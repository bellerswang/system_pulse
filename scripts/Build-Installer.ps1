param([string]$Iscc)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $repoRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$project = Join-Path $repoRoot 'src\XinweiManager\XinweiManager.csproj'
$publishDir = Join-Path $repoRoot 'artifacts\publish'
$installerDir = Join-Path $repoRoot 'artifacts\installer'
$script = Join-Path $PSScriptRoot 'SystemPulse.iss'
if (-not $Iscc) {
    $candidates = @(
        (Join-Path $repoRoot 'artifacts\tools\InnoSetup\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $Iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) {
    throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 or pass -Iscc with its full path.'
}
$env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget\packages'
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet_home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet restore $project -r win-x64 --configfile (Join-Path $repoRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o $publishDir --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
& $Iscc $script
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $installerDir 'SystemPulseSetup-1.1.0.exe'
if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer was not created.' }
Get-Item -LiteralPath $installer | Select-Object FullName,Length
