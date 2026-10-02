#requires -Version 7.4
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$PackageUrl,
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][string]$FeedPath,
    [string]$Version='0.3.0',
    [string]$ReleaseNotes=''
)
$ErrorActionPreference='Stop'
$releasePackage=(Resolve-Path -LiteralPath $PackagePath).Path
$releaseKey=(Resolve-Path -LiteralPath $PrivateKeyPath).Path
$releaseFeed=[IO.Path]::GetFullPath($FeedPath)
$parsedReleaseVersion=$null
if(-not [Version]::TryParse($Version,[ref]$parsedReleaseVersion)) {throw 'Invalid version'}
$parsedReleaseUrl=$null
if(-not [IO.Path]::IsPathFullyQualified($PackageUrl) -and (-not [Uri]::TryCreate($PackageUrl,[UriKind]::Absolute,[ref]$parsedReleaseUrl) -or $parsedReleaseUrl.Scheme -ne 'https')) {throw 'Use an HTTPS URL or absolute local package path'}
$releaseHash=(Get-FileHash -LiteralPath $releasePackage -Algorithm SHA256).Hash
$releaseSize=(Get-Item -LiteralPath $releasePackage).Length
if(-not ('PocketReleaseSigner' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
public static class PocketReleaseSigner {
  public static string Sign(string version,string url,string hash,long size,string privatePath,string expectedPublic) {
    using var key=ECDsa.Create(); key.ImportFromPem(File.ReadAllText(privatePath));
    if(key.ExportSubjectPublicKeyInfoPem().Replace("\r","").Trim()!=expectedPublic.Replace("\r","").Trim()) throw new InvalidOperationException("Private key does not match the application's public key");
    string text=version+"\n"+url+"\n"+hash.ToUpperInvariant()+"\n"+size.ToString(System.Globalization.CultureInfo.InvariantCulture);
    return Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(text),HashAlgorithmName.SHA256));
  }
}
'@
}
$releasePublic=[regex]::Match((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'UpdateKey.cs') -Raw),'-----BEGIN PUBLIC KEY-----[\s\S]*?-----END PUBLIC KEY-----').Value
$releaseSignature=[PocketReleaseSigner]::Sign($Version,$PackageUrl,$releaseHash,$releaseSize,$releaseKey,$releasePublic)
$releaseManifest=[ordered]@{Version=$Version;PackageUrl=$PackageUrl;Sha256=$releaseHash;Size=$releaseSize;Signature=$releaseSignature;ReleaseNotes=$ReleaseNotes}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($releaseFeed)) | Out-Null
[IO.File]::WriteAllText($releaseFeed,($releaseManifest|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output ('Signed release feed: '+$releaseFeed)
