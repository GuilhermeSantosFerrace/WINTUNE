<#
.SYNOPSIS
    Script de build do WinTune.

.DESCRIPTION
    Alvos disponíveis:
      build    Compila a solução (Release por padrão).
      test     Executa os testes de unidade (exclui Category=Integration).
      run      Compila e abre o aplicativo (o Windows pede permissão de administrador / UAC).
      integration Testes de integração somente leitura contra o Windows real (só na máquina local).
      format   Corrige a formatação do código (dotnet format).
      coverage Mede a cobertura do WinTune.Core e falha abaixo de 80%.
      publish  Gera executável single-file self-contained para win-x64 em .\publish.
      installer Publica e compila o instalador Inno Setup em .\artifacts (requer Inno Setup 6).
      clean    Remove bin/, obj/, publish/ e TestResults/.

.EXAMPLE
    .\build.ps1 build
    .\build.ps1 test -Configuration Debug
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'test', 'run', 'integration', 'format', 'coverage', 'publish', 'installer', 'clean')]
    [string]$Target = 'build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Versão do instalador (alvo "installer").
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$solution = Join-Path $root 'WinTune.sln'
$appProject = Join-Path $root 'src\WinTune.App\WinTune.App.csproj'
$publishDir = Join-Path $root 'publish'

# Usa o SDK instalado por usuário (dotnet-install.ps1) quando existir.
$userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) {
    $env:DOTNET_ROOT = $userDotnet
    $env:PATH = "$userDotnet;$env:PATH"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') falhou com código $LASTEXITCODE."
    }
}

switch ($Target) {
    'build' {
        Invoke-Dotnet @('build', $solution, '-c', $Configuration)
    }
    'test' {
        Invoke-Dotnet @('test', $solution, '-c', $Configuration, '--filter', 'Category!=Integration')
    }
    'run' {
        # O app exige administrador (manifest requireAdministrator). "dotnet run" falharia num terminal comum
        # (erro 740); abrir pelo shell faz o Windows mostrar o UAC.
        Invoke-Dotnet @('build', $appProject, '-c', $Configuration)
        $exe = Join-Path $root "src\WinTune.App\bin\$Configuration\net8.0-windows\WinTune.exe"
        Start-Process -FilePath $exe
    }
    'integration' {
        Invoke-Dotnet @('test', $solution, '-c', $Configuration, '--filter', 'Category=Integration')
    }
    'format' {
        Invoke-Dotnet @('format', $solution)
    }
    'coverage' {
        $results = Join-Path $root 'TestResults\coverage'
        if (Test-Path $results) { Remove-Item $results -Recurse -Force }
        Invoke-Dotnet @('test', (Join-Path $root 'tests\WinTune.Core.Tests'), '-c', $Configuration, '--filter', 'Category!=Integration',
            '--settings', (Join-Path $root 'coverage.runsettings'), '--collect:XPlat Code Coverage', '--results-directory', $results)
        $file = Get-ChildItem $results -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
        $rate = [double]([xml](Get-Content $file.FullName)).coverage.'line-rate'
        Write-Host ("Cobertura de linhas do WinTune.Core: {0:P1} (mínimo 80%)" -f $rate)
        if ($rate -lt 0.80) { throw 'Cobertura abaixo de 80%.' }
    }
    'publish' {
        Invoke-Dotnet @('publish', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained',
            '-p:PublishSingleFile=true', '-o', $publishDir)
    }
    'installer' {
        Invoke-Dotnet @('publish', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained',
            '-p:PublishSingleFile=true', '-o', $publishDir)
        & (Join-Path $root 'installer\build-installer.ps1') -Version $Version
    }
    'clean' {
        Get-ChildItem -Path $root -Directory -Recurse -Include 'bin', 'obj', 'TestResults' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\\.git\\' } |
            Remove-Item -Recurse -Force
        if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    }
}
