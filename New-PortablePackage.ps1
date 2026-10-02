#requires -Version 7.4
param(
    [Parameter(Mandatory)][string]$PublishFolder,
    [Parameter(Mandatory)][string]$PackagePath
)
$ErrorActionPreference='Stop'
$packageSource=(Resolve-Path -LiteralPath $PublishFolder).Path
$packageTarget=[IO.Path]::GetFullPath($PackagePath)
if($packageTarget.StartsWith($packageSource+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'The output archive must be outside the publish folder'}
if(Test-Path -LiteralPath $packageTarget) {throw 'Package already exists; use a fresh output path'}
foreach($required in @('PocketDeadlock.exe','PocketDeadlock.dll','PocketDeadlock.deps.json','PocketDeadlock.runtimeconfig.json','System.Private.CoreLib.dll')) {
    if(-not (Test-Path -LiteralPath (Join-Path $packageSource $required) -PathType Leaf)) {throw "Missing portable runtime file: $required"}
}
$packageMetadata=[Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $packageSource 'PocketDeadlock.dll')))
if($packageMetadata -match 'SelfTests|ReviewTileControls|ReviewCatalogControls|CapturePreviewAsync|RenderTargetBitmap') {throw 'Diagnostic code must not be shipped in the portable package'}
foreach($license in @('LICENSE','SharpCompress-LICENSE.txt','WebView2-LICENSE.txt','WebView2-NOTICE.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $license) -Destination (Join-Path $packageSource $license) -Force
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($packageTarget)) | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($packageSource,$packageTarget,[IO.Compression.CompressionLevel]::Optimal,$false)
$packageArchive=[IO.Compression.ZipFile]::OpenRead($packageTarget)
try {
    foreach($entry in $packageArchive.Entries) {
        if($entry.FullName -match '(^|/)(state\.json|mods/|gamebanana-session|gamebanana-browser/)' -or $entry.FullName -match '\.(pem|pfx|p12|pdb)$') {throw "Private or diagnostic file in release package: $($entry.FullName)"}
    }
    if(-not $packageArchive.GetEntry('PocketDeadlock.exe')) {throw 'The portable archive must contain PocketDeadlock.exe at its root'}
    Write-Output "Portable package verified: $($packageArchive.Entries.Count) files"
} finally {$packageArchive.Dispose()}
$packageHash=(Get-FileHash -LiteralPath $packageTarget -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path ([IO.Path]::GetDirectoryName($packageTarget)) 'SHA256SUMS.txt'),"$packageHash  $([IO.Path]::GetFileName($packageTarget))`n",[Text.UTF8Encoding]::new($false))
