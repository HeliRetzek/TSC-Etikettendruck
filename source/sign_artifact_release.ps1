param(
    [Parameter(Mandatory = $true)]
    [string[]]$Targets,

    [Parameter(Mandatory = $true)]
    [string]$SignToolPath,

    [Parameter(Mandatory = $true)]
    [string]$DlibPath,

    [Parameter(Mandatory = $true)]
    [string]$MetadataPath
)

$ErrorActionPreference = 'Stop'

foreach ($requiredPath in @($SignToolPath, $DlibPath, $MetadataPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Erforderliche Signing-Datei fehlt: $requiredPath"
    }
}

$files = New-Object System.Collections.Generic.List[string]
foreach ($target in $Targets) {
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        $files.Add((Resolve-Path -LiteralPath $target).Path)
    }
    elseif (Test-Path -LiteralPath $target -PathType Container) {
        Get-ChildItem -LiteralPath $target -File |
            Where-Object Extension -in @('.exe', '.dll') |
            ForEach-Object { $files.Add($_.FullName) }
    }
    else {
        throw "Signing-Ziel fehlt: $target"
    }
}

$uniqueFiles = $files | Sort-Object -Unique
foreach ($file in $uniqueFiles) {
    $existingSignature = Get-AuthenticodeSignature -LiteralPath $file
    if ($existingSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
        Write-Host "Bereits gueltig signiert: $file"
        continue
    }

    & $SignToolPath sign /v /fd SHA256 `
        /tr 'http://timestamp.acs.microsoft.com' /td SHA256 `
        /dlib $DlibPath /dmdf $MetadataPath $file
    if ($LASTEXITCODE -ne 0) {
        throw "Artifact Signing fehlgeschlagen: $file"
    }

    & $SignToolPath verify /pa /v $file
    if ($LASTEXITCODE -ne 0) {
        throw "Signaturpruefung fehlgeschlagen: $file"
    }

    $verifiedSignature = Get-AuthenticodeSignature -LiteralPath $file
    if ($verifiedSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode-Signatur ist nach dem Signieren nicht gueltig: $file"
    }
}

Write-Host "OK: $($uniqueFiles.Count) Programmdateien besitzen eine gueltige Authenticode-Signatur."
