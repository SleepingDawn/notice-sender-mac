param(
    [string]$RuntimeDirectory = '',
    [string]$PublishDirectory = '',
    [switch]$VerifyOnly,
    [switch]$FunctionsOnly
)
$ErrorActionPreference = 'Stop'

function Get-WindowsOcrPinnedHashes([string]$RecognizerPath) {
    $source = [IO.File]::ReadAllText($RecognizerPath)
    $patterns = @{
        'tesseract.exe' = 'RequireHash\(executable,\s*"([A-Fa-f0-9]{64})"\)'
        'eng.traineddata' = 'RequireHash\(Path\.Combine\(tessdata,\s*"eng\.traineddata"\),\s*"([A-Fa-f0-9]{64})"\)'
        'kor.traineddata' = 'RequireHash\(Path\.Combine\(tessdata,\s*"kor\.traineddata"\),\s*"([A-Fa-f0-9]{64})"\)'
    }
    $result = @{}
    foreach ($name in $patterns.Keys) {
        $matches = [regex]::Matches($source, $patterns[$name])
        if ($matches.Count -ne 1) { throw "Cannot read the unique recognized OCR pin: $name" }
        $result[$name] = $matches[0].Groups[1].Value.ToUpperInvariant()
    }
    return $result
}

function Assert-OcrPlainPath([string]$Path) {
    for ($item = [IO.Path]::GetFullPath($Path); $item; $item = [IO.Path]::GetDirectoryName($item)) {
        if (([IO.File]::GetAttributes($item) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "OCR path contains a reparse point: $item" }
    }
}
function Get-OcrFileRecord([string]$Root, [string]$RelativePath, [string]$DestinationPath, [string]$Kind) {
    $path = Join-Path $Root $RelativePath
    if (![IO.File]::Exists($path)) { throw "OCR runtime file is missing: $RelativePath" }
    Assert-OcrPlainPath $path
    $length = ([IO.FileInfo]$path).Length
    if ($length -le 0) { throw "OCR runtime file has invalid length: $RelativePath" }
    return [pscustomobject]@{ SourcePath=$path; RelativePath=$DestinationPath; Length=$length; Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant(); Kind=$Kind }
}
function Get-OcrSourceFiles([string]$Root) {
    foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($Root)) {
        $attributes = [IO.File]::GetAttributes($entry)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "OCR source contains a reparse point: $entry" }
        if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) { Get-OcrSourceFiles $entry }
        else { $entry }
    }
}
function Get-WindowsOcrRuntimePlan([string]$RuntimeDirectory, [Collections.IDictionary]$PinnedHashes) {
    if (!$RuntimeDirectory -or ![IO.Directory]::Exists($RuntimeDirectory)) { throw 'OCR runtime directory is missing.' }
    $root = [IO.Path]::GetFullPath($RuntimeDirectory).TrimEnd('\', '/')
    Assert-OcrPlainPath $root
    $manifestPath = Join-Path $root 'runtime-dependency-manifest.json'
    if (![IO.File]::Exists($manifestPath)) { throw 'OCR runtime hash manifest is missing.' }
    Assert-OcrPlainPath $manifestPath
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if (!$manifest.engine -or !@($manifest.files).Count) { throw 'OCR runtime manifest requires engine and files.' }
    $declared = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $allowedSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $records = [Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    [void]$allowedSources.Add('runtime-dependency-manifest.json')
    foreach ($entry in @($manifest.files)) {
        $name = [string]$entry.name
        if ($name -notmatch '\A[A-Za-z0-9+_.-]+\z' -or $name -in @('.', '..') -or $name -notmatch '\.(dll|exe|traineddata)\z') { throw "Invalid OCR manifest file name/path: $name" }
        if (!$declared.Add($name)) { throw "Duplicate OCR manifest name: $name" }
        $length = 0L
        if (![long]::TryParse([string]$entry.length, [ref]$length) -or $length -le 0 -or [string]$entry.sha256 -notmatch '\A[A-Fa-f0-9]{64}\z') { throw "Invalid OCR manifest length/hash: $name" }
        $model = $name -in @('eng.traineddata', 'kor.traineddata')
        if (($name.EndsWith('.traineddata', [StringComparison]::OrdinalIgnoreCase) -and !$model) -or ($name.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase) -and $name -ine 'tesseract.exe')) { throw "Unexpected OCR manifest payload: $name" }
        $destination = if ($model) { 'tessdata/' + $name.ToLowerInvariant() } elseif ($name -ieq 'tesseract.exe') { 'tesseract.exe' } else { $name }
        $sourceNames = @($name)
        if ($model) { $sourceNames = @($name, $destination) }
        $record = $null
        foreach ($sourceName in $sourceNames) {
            if (![IO.File]::Exists((Join-Path $root $sourceName))) { continue }
            $current = Get-OcrFileRecord $root $sourceName $destination 'runtime'
            if ($current.Length -ne $length -or $current.Sha256 -ine [string]$entry.sha256) { throw "OCR manifest hash/length mismatch: $sourceName" }
            if ($PinnedHashes.Contains($name) -and $current.Sha256 -ine $PinnedHashes[$name]) { throw "OCR recognized identity pin mismatch: $name" }
            [void]$allowedSources.Add($sourceName.Replace('\', '/'))
            $record = $current
        }
        if ($null -eq $record) { throw "OCR runtime file is missing: $name" }
        $records.Add($destination, $record)
    }
    foreach ($required in @('tesseract.exe', 'eng.traineddata', 'kor.traineddata')) {
        if (!$declared.Contains($required) -or !$PinnedHashes.Contains($required) -or $PinnedHashes[$required] -notmatch '\A[A-Fa-f0-9]{64}\z') { throw "Missing recognized OCR identity pin/model: $required" }
    }
    foreach ($required in @('doc/LICENSE', 'doc/AUTHORS', 'doc/README.md')) {
        if (![IO.File]::Exists((Join-Path $root $required))) { throw "OCR bundled notice is missing: $required" }
    }
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    foreach ($path in @(Get-OcrSourceFiles $root)) {
        $relative = $path.Substring($prefix.Length).Replace('\', '/')
        if ($relative.StartsWith('doc/', [StringComparison]::OrdinalIgnoreCase)) {
            if ($relative -match '(?i)\.(exe|dll|traineddata)\z') { throw "Unexpected executable/data in OCR notices: $relative" }
            $notice = Get-OcrFileRecord $root $relative $relative 'notice'
            $records.Add($relative, $notice)
            [void]$allowedSources.Add($relative)
        }
        if (!$allowedSources.Contains($relative)) { throw "Undeclared/unexpected OCR source file: $relative" }
    }
    return [pscustomobject]@{
        SourceRoot=$root; Engine=[string]$manifest.engine; Files=@($records.Values)
        InputManifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

function Copy-WindowsOcrRuntime($Plan, [string]$PublishDirectory) {
    if (!$PublishDirectory) { throw 'OCR publish directory is required.' }
    $publish = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\', '/')
    $source = [IO.Path]::GetFullPath($Plan.SourceRoot).TrimEnd('\', '/')
    if ($publish.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or $publish.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase) -or $source.StartsWith($publish + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'OCR source and publish directories overlap.' }
    [IO.Directory]::CreateDirectory($publish) | Out-Null
    Assert-OcrPlainPath $publish
    $target = [IO.Path]::GetFullPath((Join-Path $publish 'ocr'))
    $stage = [IO.Path]::GetFullPath((Join-Path $publish ('ocr.staging-' + [guid]::NewGuid().ToString('N'))))
    if ([IO.Path]::GetDirectoryName($target) -ne $publish -or [IO.Path]::GetDirectoryName($stage) -ne $publish) { throw 'Unexpected OCR output target.' }
    try {
        [IO.Directory]::CreateDirectory($stage) | Out-Null
        foreach ($file in $Plan.Files) {
            $destination = [IO.Path]::GetFullPath((Join-Path $stage $file.RelativePath))
            if (!$destination.StartsWith($stage + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'OCR output path escapes its staging directory.' }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            [IO.File]::Copy($file.SourcePath, $destination, $false)
            if (([IO.FileInfo]$destination).Length -ne $file.Length -or (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ine $file.Sha256) { throw "OCR bytes changed during copy: $($file.RelativePath)" }
        }
        $canonical = [ordered]@{
            schema=1; engine=$Plan.Engine; source_manifest_sha256=$Plan.InputManifestSha256
            identity_basis='Engine and eng/kor hashes are pinned by AttachmentVisualRecognizer; every declared dependency matches the supplied manifest. Notice hashes record copied bytes, not executable trust.'
            files=@($Plan.Files | ForEach-Object { [ordered]@{ path=$_.RelativePath; length=$_.Length; sha256=$_.Sha256; kind=$_.Kind } })
        }
        [IO.File]::WriteAllText((Join-Path $stage 'runtime-dependency-manifest.json'), ($canonical | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $target) {
            Assert-OcrPlainPath $target
            # Check the complete existing subtree before replacing this exact owned output.
            @(Get-OcrSourceFiles $target) | Out-Null
            if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($target)) -ne $publish -or [IO.Path]::GetFileName($target) -ne 'ocr') { throw 'Unexpected OCR replacement target.' }
            Remove-Item -LiteralPath $target -Recurse -Force
        }
        Move-Item -LiteralPath $stage -Destination $target
        Write-Output "OCR runtime packaged: $($Plan.Files.Count) payload/notice files in $target"
    } finally {
        if (Test-Path -LiteralPath $stage) {
            if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($stage)) -ne $publish -or [IO.Path]::GetFileName($stage) -notlike 'ocr.staging-*') { throw 'Unexpected OCR staging cleanup target.' }
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
}

if ($FunctionsOnly) { return }
$repository = Split-Path $PSScriptRoot -Parent
$pins = Get-WindowsOcrPinnedHashes -RecognizerPath (Join-Path $repository 'Windows\NoticeSender.Windows\AttachmentVisualRecognizer.cs')
$plan = Get-WindowsOcrRuntimePlan -RuntimeDirectory $RuntimeDirectory -PinnedHashes $pins
if ($VerifyOnly) { Write-Output "OCR runtime validated: $($plan.Files.Count) payload/notice files; no executable was run." }
else { Copy-WindowsOcrRuntime -Plan $plan -PublishDirectory $PublishDirectory }
