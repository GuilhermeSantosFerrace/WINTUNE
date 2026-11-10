<#
.SYNOPSIS
    Compila o instalador do WinTune com o Inno Setup 6 (usa o ISCC instalado; no CI instala uma versão fixa via Chocolatey).
.PARAMETER Version
    Versão SemVer (ex.: 1.0.0).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $root 'publish\WinTune.exe'))) {
    throw 'publish\WinTune.exe não encontrado. Rode antes: .\build.ps1 publish'
}

$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    if ($env:CI -eq 'true' -and (Get-Command choco -ErrorAction SilentlyContinue)) {
        choco install innosetup --version=6.4.3 -y --no-progress | Out-Host
        $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
}

if (-not $iscc) {
    throw 'Inno Setup 6 não encontrado. Instale em https://jrsoftware.org/isinfo.php e rode de novo.'
}

& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'WinTune.iss')
if ($LASTEXITCODE -ne 0) {
    throw "ISCC falhou com código $LASTEXITCODE."
}
