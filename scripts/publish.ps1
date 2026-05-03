#requires -Version 7.0
<#
.SYNOPSIS
    Локальная публикация PoproshaykaBot — повторяет release.yml.

.DESCRIPTION
    Собирает framework-dependent и portable single-file сборки под win-x64/win-x86,
    переименовывает их в PoproshaykaBot-v<version>-<arch>[-portable].exe и упаковывает
    в одноимённые .zip. Артефакты складываются в <repo>/artifacts.

.PARAMETER Version
    Версия для подстановки в -p:Version. По умолчанию читается из Directory.Build.props.

.PARAMETER Arch
    Архитектуры для сборки. По умолчанию — обе (x64, x86).

.PARAMETER Variant
    Варианты сборки: framework-dependent, portable. По умолчанию — оба.

.PARAMETER OutputDir
    Куда сложить итоговые .exe и .zip. По умолчанию <repo>/artifacts.

.EXAMPLE
    pwsh ./scripts/publish.ps1
    pwsh ./scripts/publish.ps1 -Arch x64 -Variant portable
    pwsh ./scripts/publish.ps1 -Version 3.0.0.4
#>

[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('x64', 'x86')]
    [string[]]$Arch = @('x64', 'x86'),
    [ValidateSet('framework-dependent', 'portable')]
    [string[]]$Variant = @('framework-dependent', 'portable'),
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectDir = Join-Path $repoRoot 'PoproshaykaBot.WinForms'
$propsPath = Join-Path $repoRoot 'Directory.Build.props'

if (-not $Version) {
    [xml]$props = Get-Content -LiteralPath $propsPath
    $Version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) {
        throw "Не удалось извлечь <Version> из $propsPath."
    }
}

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot 'artifacts'
}

Write-Host "Версия: $Version"
Write-Host "Архитектуры: $($Arch -join ', ')"
Write-Host "Варианты: $($Variant -join ', ')"
Write-Host "Выходная папка: $OutputDir"

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$publishRoot = Join-Path $projectDir 'publish'
if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

function Invoke-DotnetPublish {
    param(
        [string]$Arch,
        [string]$Variant
    )

    $variantDir = Join-Path $publishRoot $Variant
    $args = @(
        'publish',
        '--configuration', 'Release',
        '--runtime', "win-$Arch",
        '--output', $variantDir,
        '-p:PublishSingleFile=true',
        '-p:PublishReadyToRun=true',
        "-p:Version=$Version"
    )

    if ($Variant -eq 'portable') {
        $args += @(
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:PortableMode=true',
            '--self-contained'
        )
    }
    else {
        $args += '--no-self-contained'
    }

    Write-Host ""
    Write-Host "==> dotnet $($args -join ' ')"
    Push-Location $projectDir
    try {
        & dotnet @args
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish завершился с кодом $LASTEXITCODE (arch=$Arch, variant=$Variant)."
        }
    }
    finally {
        Pop-Location
    }

    $sourceExe = Join-Path $variantDir 'PoproshaykaBot.WinForms.exe'
    if (-not (Test-Path -LiteralPath $sourceExe)) {
        throw "Ожидаемый файл не найден: $sourceExe"
    }

    $suffix = if ($Variant -eq 'portable') { "-$Arch-portable" } else { "-$Arch" }
    $exeName = "PoproshaykaBot-v$Version$suffix.exe"
    $zipName = "PoproshaykaBot-v$Version$suffix.zip"
    $exePath = Join-Path $OutputDir $exeName
    $zipPath = Join-Path $OutputDir $zipName

    if (Test-Path -LiteralPath $exePath) { Remove-Item -LiteralPath $exePath -Force }
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

    Move-Item -LiteralPath $sourceExe -Destination $exePath
    Compress-Archive -Path $exePath -DestinationPath $zipPath

    Write-Host "  -> $exeName"
    Write-Host "  -> $zipName"
}

foreach ($a in $Arch) {
    foreach ($v in $Variant) {
        Invoke-DotnetPublish -Arch $a -Variant $v
    }
}

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

Write-Host ""
Write-Host "Готово. Артефакты в $OutputDir"
