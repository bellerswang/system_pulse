$ErrorActionPreference = 'Stop'
if (Get-Process -Name XinweiManager,SystemPulse -ErrorAction SilentlyContinue) {
    throw 'Exit System Pulse from the tray before removing the local prototype.'
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'XinweiManager' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $runKey -Name 'SystemPulse' -ErrorAction SilentlyContinue
$repoRoot = Split-Path -Parent $PSScriptRoot
$namedTarget = Join-Path $repoRoot 'app'
$target = [IO.Path]::GetFullPath($namedTarget)
$parent = [IO.Path]::GetFullPath($repoRoot)
if (-not [string]::Equals([IO.Path]::GetDirectoryName($target), $parent, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals([IO.Path]::GetFileName($target), 'app', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected target path; removal cancelled.'
}
foreach ($checkPath in @($parent, $target)) {
    if ((Test-Path -LiteralPath $checkPath) -and ((Get-Item -LiteralPath $checkPath).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Removal target cannot be a link or junction.'
    }
}
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
Write-Output 'Local prototype and launch at sign-in entry removed.'
