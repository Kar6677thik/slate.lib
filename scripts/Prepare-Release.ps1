param(
    [Parameter(Mandatory = $true)][string]$WindowsInput,
    [Parameter(Mandatory = $true)][string]$AndroidInput,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$ReleaseNotes = "Slate release"
)

$ErrorActionPreference = 'Stop'
[xml]$props = Get-Content (Join-Path $PSScriptRoot '..\Directory.Build.props')
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/SlateVersion')
$version = if ($versionNode) { $versionNode.InnerText.Trim() } else { '' }
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Directory.Build.props must contain a numeric SlateVersion.' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") { throw "Tag $($env:GITHUB_REF_NAME) does not match v$version." }

$windows = Get-ChildItem $WindowsInput -Recurse -File -Filter *.msix | Sort-Object Length -Descending | Select-Object -First 1
$android = Get-ChildItem $AndroidInput -Recurse -File -Filter *Signed.apk | Sort-Object Length -Descending | Select-Object -First 1
if (-not $windows) { throw 'No Windows MSIX package was found.' }
if (-not $android) { throw 'No signed Android APK was found.' }

New-Item -ItemType Directory -Force $Output | Out-Null
$windowsName = "Slate-$version.msix"
$androidName = "Slate-$version.apk"
$windowsOut = Join-Path $Output $windowsName
$androidOut = Join-Path $Output $androidName
Copy-Item $windows.FullName $windowsOut -Force
Copy-Item $android.FullName $androidOut -Force

function Artifact([string]$path, [string]$name) {
    $item = Get-Item $path
    @{
        fileName = $name
        url = "/v1/releases/$([Uri]::EscapeDataString($name))"
        byteSize = $item.Length
        sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    latestVersion = $version
    releasedAt = [DateTimeOffset]::UtcNow.ToString('O')
    releaseNotes = $ReleaseNotes
    windows = Artifact $windowsOut $windowsName
    android = Artifact $androidOut $androidName
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Output 'release-manifest.json') -Encoding utf8NoBOM
Get-FileHash $windowsOut, $androidOut -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content (Join-Path $Output 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Prepared Slate $version release metadata in $Output"
