<#
.SYNOPSIS
    Script de build do WinTune.

.DESCRIPTION
    Alvos disponíveis:
      build    Compila a solução (Release por padrão).
      test     Executa os testes de unidade (exclui Category=Integration).
      run      Executa o aplicativo WPF.
      publish  Gera executável single-file self-contained para win-x64 em .\publish.
      clean    Remove bin/, obj/, publish/ e TestResults/.

.EXAMPLE
    .\build.ps1 build
    .\build.ps1 test -Configuration Debug
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'test', 'run', 'publish', 'clean')]
    [string]$Target = 'build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
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
        Invoke-Dotnet @('run', '--project', $appProject, '-c', $Configuration)
    }
    'publish' {
        Invoke-Dotnet @('publish', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained',
            '-p:PublishSingleFile=true', '-o', $publishDir)
    }
    'clean' {
        Get-ChildItem -Path $root -Directory -Recurse -Include 'bin', 'obj', 'TestResults' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\\.git\\' } |
            Remove-Item -Recurse -Force
        if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    }
}
