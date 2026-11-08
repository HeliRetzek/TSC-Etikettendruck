param(
    [switch]$AlsoArm64,
    [switch]$AlsoBrainOfficeDeployment,
    [switch]$AlsoPortableFolders,
    [switch]$SkipDeploy,
    [switch]$SmokeTest,
    [string]$ArtifactSigningMetadata,
    [string]$SignToolPath,
    [string]$ArtifactSigningDlibPath,
    [switch]$AllowUnsignedLocalDeployment
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $PSScriptRoot 'TscLabel.App\TscLabel.App.vbproj'
$testProject = Join-Path $PSScriptRoot 'TscLabel.Tests\TscLabel.Tests.vbproj'
$buildRoot = Join-Path $projectRoot 'builds'
$archiveRoot = Join-Path $projectRoot 'archive\release-backups'
$smokeImage = Join-Path $buildRoot 'ui-smoke.png'
$appsX64Target = 'E:\AI\APPS\TSC-Etikettendruck.exe'
$appsArm64Target = 'E:\AI\APPS\TSC-Etikettendruck-ARM64.exe'
$releaseStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$versionNode = Select-Xml -Path $appProject -XPath '/Project/PropertyGroup/VersionPrefix' | Select-Object -First 1
if (-not $versionNode) { throw 'VersionPrefix fehlt im App-Projekt.' }
$appVersion = $versionNode.Node.InnerText
$buildStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$displayVersion = "v$appVersion · Build $([DateTime]::ParseExact($buildStamp, 'yyyyMMdd-HHmmss', $null).ToString('dd.MM.yyyy HH:mm:ss'))"

if (-not $SkipDeploy -and -not $AllowUnsignedLocalDeployment -and [string]::IsNullOrWhiteSpace($ArtifactSigningMetadata)) {
    throw 'Fremd-PC-Deployment gesperrt: Artifact-Signing-Metadaten fehlen. Unsigned Builds duerfen nur mit -SkipDeploy oder ausdruecklich als lokaler Test mit -AllowUnsignedLocalDeployment erzeugt werden.'
}

dotnet run --project $testProject -c Release -p:BuildStamp=$buildStamp -- --render $smokeImage
if ($LASTEXITCODE -ne 0) { throw 'Synthetische Tests fehlgeschlagen.' }

function Publish-TscLabel([string]$runtime) {
    $target = Join-Path $buildRoot $runtime
    dotnet publish $appProject -c Release -r $runtime --self-contained true -o $target `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=false `
        -p:SatelliteResourceLanguages=de `
        -p:BuildStamp=$buildStamp `
        -p:DebugType=None `
        -p:DebugSymbols=false | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "Publish fuer $runtime fehlgeschlagen." }

    $exe = Join-Path $target 'TSC-Etikettendruck.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Publish-Datei fehlt: $exe" }
    $unexpectedFiles = @(Get-ChildItem -LiteralPath $target -File | Where-Object Name -ne 'TSC-Etikettendruck.exe')
    if ($unexpectedFiles.Count -gt 0) {
        throw "Der $runtime-Publish ist kein reiner Single-File-Build: $($unexpectedFiles.Name -join ', ')"
    }
    if ((Get-Item -LiteralPath $exe).Length -lt 50MB) {
        throw "Der $runtime-Publish ist unerwartet klein und vermutlich nicht self-contained."
    }
    return $exe
}

function Publish-BrainOfficeTscLabel {
    $target = Join-Path $buildRoot 'brainoffice-win-x64'
    dotnet publish $appProject -c Release -r 'win-x64' --self-contained false -o $target `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false `
        -p:EnableCompressionInSingleFile=false `
        -p:SatelliteResourceLanguages=de `
        -p:BuildStamp=$buildStamp `
        -p:DebugType=None `
        -p:DebugSymbols=false | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'BrainOffice-Publish fuer win-x64 fehlgeschlagen.' }

    $exe = Join-Path $target 'TSC-Etikettendruck.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "BrainOffice-Publish-Datei fehlt: $exe" }
    $unexpectedFiles = @(Get-ChildItem -LiteralPath $target -File | Where-Object Name -ne 'TSC-Etikettendruck.exe')
    if ($unexpectedFiles.Count -gt 0) {
        throw "Der BrainOffice-Publish ist keine reine Single-File-App: $($unexpectedFiles.Name -join ', ')"
    }
    $length = (Get-Item -LiteralPath $exe).Length
    if ($length -lt 1MB -or $length -gt 50MB) {
        throw "Der BrainOffice-Publish hat eine unerwartete Groesse: $length Bytes."
    }
    return $exe
}

function Publish-PortableFolder([string]$runtime) {
    $folderName = "TSC-Etikettendruck-$runtime-portabel-$appVersion-$buildStamp"
    $target = Join-Path $buildRoot $folderName
    dotnet publish $appProject -c Release -r $runtime --self-contained true -o $target `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false `
        -p:BuildStamp=$buildStamp `
        -p:DebugType=None `
        -p:DebugSymbols=false | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "Portabler Ordner-Publish fuer $runtime fehlgeschlagen." }

    $exe = Join-Path $target 'TSC-Etikettendruck.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Portable Startdatei fehlt: $exe" }
    if (@(Get-ChildItem -LiteralPath $target -File).Count -lt 10) {
        throw "Der portable Ordner fuer $runtime enthaelt unerwartet wenige Laufzeitdateien."
    }
    return $target
}

function Backup-ExistingApp([string]$pathValue) {
    if (-not (Test-Path -LiteralPath $pathValue)) { return }
    $backupDirectory = Join-Path $archiveRoot $releaseStamp
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $backupPath = Join-Path $backupDirectory ([IO.Path]::GetFileName($pathValue))
    Copy-Item -LiteralPath $pathValue -Destination $backupPath
    $sourceHash = (Get-FileHash -LiteralPath $pathValue -Algorithm SHA256).Hash
    $backupHash = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash
    if ($sourceHash -ne $backupHash) { throw "Backup-Pruefung fehlgeschlagen: $pathValue" }
}

function Deploy-Verified([string]$source, [string]$target) {
    Backup-ExistingApp $target
    Copy-Item -LiteralPath $source -Destination $target -Force
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) { throw "Deploy-Pruefung fehlgeschlagen: $target" }
}

function Test-PortableStart([string]$exe) {
    $oldDotnetRoot = $env:DOTNET_ROOT
    $oldDotnetRootX64 = $env:DOTNET_ROOT_X64
    $oldMultilevel = $env:DOTNET_MULTILEVEL_LOOKUP
    $process = $null
    try {
        # Ein self-contained Build muss auch bei absichtlich ungueltigem DOTNET_ROOT starten.
        $env:DOTNET_ROOT = 'X:\dotnet-not-installed'
        $env:DOTNET_ROOT_X64 = 'X:\dotnet-not-installed'
        $env:DOTNET_MULTILEVEL_LOOKUP = '0'
        $process = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
        $deadline = (Get-Date).AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 250
            $process.Refresh()
            if ($process.HasExited) { throw "Smoke-Test-Prozess endete vorzeitig mit Code $($process.ExitCode)." }
            if ($process.MainWindowHandle -ne 0 -and $process.Responding) { return }
        } while ((Get-Date) -lt $deadline)
        throw 'Smoke-Test: Innerhalb von 20 Sekunden erschien kein reagierendes Fenster.'
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit(5000) | Out-Null
        }
        $env:DOTNET_ROOT = $oldDotnetRoot
        $env:DOTNET_ROOT_X64 = $oldDotnetRootX64
        $env:DOTNET_MULTILEVEL_LOOKUP = $oldMultilevel
    }
}

$x64Exe = Publish-TscLabel 'win-x64'
$arm64Exe = $null
if ($AlsoArm64) {
    $arm64Exe = Publish-TscLabel 'win-arm64'
}

$brainOfficeExe = $null
if ($AlsoBrainOfficeDeployment) {
    $brainOfficeExe = Publish-BrainOfficeTscLabel
}

$x64PortableFolder = $null
$arm64PortableFolder = $null
if ($AlsoPortableFolders) {
    $x64PortableFolder = Publish-PortableFolder 'win-x64'
    if ($AlsoArm64) {
        $arm64PortableFolder = Publish-PortableFolder 'win-arm64'
    }
}

if (-not [string]::IsNullOrWhiteSpace($ArtifactSigningMetadata)) {
    if ([string]::IsNullOrWhiteSpace($SignToolPath) -or -not (Test-Path -LiteralPath $SignToolPath)) {
        throw 'Artifact Signing angefordert, aber SignToolPath ist ungueltig.'
    }
    if ([string]::IsNullOrWhiteSpace($ArtifactSigningDlibPath) -or -not (Test-Path -LiteralPath $ArtifactSigningDlibPath)) {
        throw 'Artifact Signing angefordert, aber ArtifactSigningDlibPath ist ungueltig.'
    }
    if (-not (Test-Path -LiteralPath $ArtifactSigningMetadata)) {
        throw 'Artifact Signing angefordert, aber die Metadatendatei fehlt.'
    }

    $signTargets = @($x64Exe)
    if ($arm64Exe) { $signTargets += $arm64Exe }
    if ($brainOfficeExe) { $signTargets += $brainOfficeExe }
    if ($x64PortableFolder) { $signTargets += $x64PortableFolder }
    if ($arm64PortableFolder) { $signTargets += $arm64PortableFolder }

    & (Join-Path $PSScriptRoot 'sign_artifact_release.ps1') `
        -Targets $signTargets `
        -SignToolPath $SignToolPath `
        -DlibPath $ArtifactSigningDlibPath `
        -MetadataPath $ArtifactSigningMetadata
    if ($LASTEXITCODE -ne 0) { throw 'Artifact Signing ist fehlgeschlagen.' }
}

function Assert-ValidSignature([string]$pathValue) {
    $signature = Get-AuthenticodeSignature -LiteralPath $pathValue
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Ungueltige oder fehlende Authenticode-Signatur ($($signature.Status)): $pathValue"
    }
}

if (-not $SkipDeploy -and -not $AllowUnsignedLocalDeployment) {
    Assert-ValidSignature $x64Exe
    if ($arm64Exe) { Assert-ValidSignature $arm64Exe }
    foreach ($folder in @($x64PortableFolder, $arm64PortableFolder)) {
        if ($folder) {
            Get-ChildItem -LiteralPath $folder -File |
                Where-Object Extension -in @('.exe', '.dll') |
                ForEach-Object { Assert-ValidSignature $_.FullName }
        }
    }
}

if ($SmokeTest) {
    Test-PortableStart $x64Exe
}

if (-not $SkipDeploy) {
    Deploy-Verified $x64Exe $appsX64Target
    if ($arm64Exe) {
        Deploy-Verified $arm64Exe $appsArm64Target
    }
}

$manifest = [ordered]@{
    builtAt = (Get-Date).ToString('o')
    version = $appVersion
    buildStamp = $buildStamp
    displayVersion = $displayVersion
    framework = 'net10.0-windows10.0.19041.0'
    packaging = 'self-contained single-file'
    x64 = [ordered]@{
        path = $x64Exe
        sha256 = (Get-FileHash -LiteralPath $x64Exe -Algorithm SHA256).Hash
        bytes = (Get-Item -LiteralPath $x64Exe).Length
        deployedTo = $(if ($SkipDeploy) { $null } else { $appsX64Target })
    }
    arm64 = $(if ($arm64Exe) {
        [ordered]@{
            path = $arm64Exe
            sha256 = (Get-FileHash -LiteralPath $arm64Exe -Algorithm SHA256).Hash
            bytes = (Get-Item -LiteralPath $arm64Exe).Length
            deployedTo = $(if ($SkipDeploy) { $null } else { $appsArm64Target })
        }
    } else { $null })
    brainOffice = $(if ($brainOfficeExe) {
        [ordered]@{
            path = $brainOfficeExe
            sha256 = (Get-FileHash -LiteralPath $brainOfficeExe -Algorithm SHA256).Hash
            bytes = (Get-Item -LiteralPath $brainOfficeExe).Length
            packaging = 'framework-dependent single-file'
            runtime = 'Microsoft.WindowsDesktop.App 10.0 x64'
        }
    } else { $null })
    portableFolders = [ordered]@{
        x64 = $x64PortableFolder
        arm64 = $arm64PortableFolder
    }
    externalRequirements = @(
        'Windows 10 2004 (Build 19041) oder neuer',
        'Passende CPU-Datei: x64 oder ARM64',
        'Schreibbarer Benutzer-TEMP-Ordner fuer Single-File-Extraktion',
        'Fuer Netzwerkdruck: TSPL- oder ZPL-Drucker ueber TCP/IP Port 9100 erreichbar',
        'Windows-OCR-Sprachpaket nur fuer die optionale Clipboard-OCR-Funktion'
    )
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $buildRoot 'PORTABLE_RELEASE_MANIFEST.json') -Encoding utf8

if ($SkipDeploy) {
    Write-Host "x64-Build separat bereit (APPS nicht veraendert): $x64Exe"
} else {
    Write-Host "x64-Release bereit: $appsX64Target"
}
Write-Host "Buildkennung: $displayVersion"
if ($arm64Exe) {
    if ($SkipDeploy) {
        Write-Host "ARM64-Build separat bereit (APPS nicht veraendert): $arm64Exe"
    } else {
        Write-Host "ARM64-Release bereit: $appsArm64Target"
    }
}
if ($brainOfficeExe) {
    Write-Host "Kleiner BrainOffice-Build ohne eingebettete Runtime: $brainOfficeExe"
}
if ($x64PortableFolder) { Write-Host "Robuster x64-Ordner bereit: $x64PortableFolder" }
if ($arm64PortableFolder) { Write-Host "Robuster ARM64-Ordner bereit: $arm64PortableFolder" }
