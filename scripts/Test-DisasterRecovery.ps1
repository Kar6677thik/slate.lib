param(
    [string]$ResticPath = 'restic',
    [switch]$KeepTemporaryData
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("slate-dr-test-" + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$resticRepository = Join-Path $root 'restic-repository'
$restoreTarget = Join-Path $root 'restored'
$server = $null
$oldRepository = $env:RESTIC_REPOSITORY
$oldPassword = $env:RESTIC_PASSWORD

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}

function Remove-DisposableTree([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $container = if ((Split-Path $resolved -Leaf).StartsWith('slate-dr-test-')) { $resolved } else { Split-Path $resolved -Parent }
    if (-not (Split-Path $container -Leaf).StartsWith('slate-dr-test-') -or -not $resolved.StartsWith($temp)) { throw "Unsafe disposable cleanup path: $resolved" }
    if (-not (Test-Path -LiteralPath $resolved)) { return }
    if ($IsWindows) {
        & icacls.exe $resolved /reset /T /C | Out-Null
        & icacls.exe $resolved /grant:r "$env:USERNAME`:(OI)(CI)F" /T /C | Out-Null
    }
    Get-ChildItem -LiteralPath $resolved -Force -Recurse -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Attributes = [IO.FileAttributes]::Normal } catch {} }
    try { (Get-Item -LiteralPath $resolved -Force).Attributes = [IO.FileAttributes]::Normal } catch {}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

try {
    $library = Join-Path $source 'library'
    $assets = Join-Path $source 'assets'
    $auth = Join-Path $source 'auth'
    $state = Join-Path $source 'state'
    New-Item -ItemType Directory -Force (Join-Path $library '.slate'), (Join-Path $assets 'objects'), (Join-Path $assets 'metadata'), $auth, $state | Out-Null
    $libraryId = [Guid]::NewGuid(); $noteA = [Guid]::NewGuid(); $noteB = [Guid]::NewGuid(); $assetId = [Guid]::NewGuid()
    @{ schemaVersion = 1; libraryId = $libraryId } | ConvertTo-Json | Set-Content (Join-Path $library '.slate/library.json') -Encoding utf8NoBOM
    $assetBytes = [byte[]](0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a,1,2,3)
    $assetPath = Join-Path $assets "objects/$assetId.png"; [IO.File]::WriteAllBytes($assetPath, $assetBytes)
    $assetHash = (Get-FileHash $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    @{ id = $assetId; originalFilename = 'restore.png'; contentType = 'image/png'; byteSize = $assetBytes.Length; createdAt = [DateTimeOffset]::UtcNow; sha256 = $assetHash; extension = '.png'; inlineImage = $true } |
        ConvertTo-Json | Set-Content (Join-Path $assets "metadata/$assetId.json") -Encoding utf8NoBOM
    @"
---
id: $noteA
---
# Note A

recoverable-cobalt

![Recovered image](.assets/$assetId.png)
"@ | Set-Content (Join-Path $library 'Note A.md') -Encoding utf8NoBOM
    @"
---
id: $noteB
---
# Note B

[[Note A]]
"@ | Set-Content (Join-Path $library 'Note B.md') -Encoding utf8NoBOM
    $token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $tokenHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($token))).ToLowerInvariant()
    @{ schemaVersion = 1; devices = @(@{ id = [Guid]::NewGuid(); label = 'Restore test'; tokenHash = $tokenHash; createdAt = [DateTimeOffset]::UtcNow; lastUsedAt = $null; revokedAt = $null }) } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $auth 'devices.json') -Encoding utf8NoBOM

    Invoke-Checked git @('-C', $library, 'init', '-b', 'main')
    Invoke-Checked git @('-C', $library, 'config', 'user.name', 'Restore Test')
    Invoke-Checked git @('-C', $library, 'config', 'user.email', 'restore@example.invalid')
    Invoke-Checked git @('-C', $library, 'config', 'core.autocrlf', 'false')
    Invoke-Checked git @('-C', $library, 'add', '-A')
    Invoke-Checked git @('-C', $library, 'commit', '-m', 'Disaster recovery fixture')

    $env:RESTIC_REPOSITORY = $resticRepository; $env:RESTIC_PASSWORD = 'slate-disposable-restore-test'
    Invoke-Checked $ResticPath @('init')
    Invoke-Checked $ResticPath @('backup', '--tag', 'slate-dr-test', $source)
    Remove-DisposableTree $source
    & $ResticPath restore latest --target $restoreTarget
    $restoreExit = $LASTEXITCODE
    Invoke-Checked $ResticPath @('check', '--read-data')

    $identities = @(Get-ChildItem $restoreTarget -Recurse -File -Filter library.json | Where-Object { $_.Directory.Name -eq '.slate' })
    if ($identities.Count -ne 1) { throw "Restic restore failed with exit code $restoreExit or restored an ambiguous Library layout." }
    $identity = $identities[0]
    $restoredLibrary = $identity.Directory.Parent.FullName
    $restoredData = Split-Path $restoredLibrary -Parent
    $restoredAssets = Join-Path $restoredData 'assets'; $restoredAuth = Join-Path $restoredData 'auth/devices.json'; $restoredState = Join-Path $restoredData 'state'
    if (-not (Test-Path (Join-Path $restoredAssets "objects/$assetId.png")) -or -not (Test-Path $restoredAuth)) { throw 'Durable restore set is incomplete.' }

    Invoke-Checked dotnet @('build', (Join-Path $repositoryRoot 'src/Slate.Lib.Api/Slate.Lib.Api.csproj'), '-c', 'Release', '--no-restore')
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
    $url = "http://127.0.0.1:$port"
    $env:Library__RootPath = $restoredLibrary; $env:Data__AssetsPath = $restoredAssets; $env:Data__DerivedPath = Join-Path $root 'rebuilt-derived'
    $env:Authentication__DeviceFile = $restoredAuth; $env:Git__StatePath = $restoredState; $env:Git__SyncIntervalSeconds = '3600'
    $apiDll = Join-Path $repositoryRoot 'src/Slate.Lib.Api/bin/Release/net10.0/Slate.Lib.Api.dll'
    $server = Start-Process dotnet -ArgumentList @("`"$apiDll`"", '--urls', $url) -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root 'api.log') -RedirectStandardError (Join-Path $root 'api.err.log')
    $live = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) { try { Invoke-RestMethod "$url/health/ready" | Out-Null; $live = $true; break } catch { Start-Sleep -Milliseconds 250 } }
    if (-not $live) { throw 'Restored Slate API did not become ready.' }
    $headers = @{ Authorization = "Bearer $token" }
    $search = Invoke-RestMethod "$url/v1/search?q=recoverable-cobalt&page=0&pageSize=20" -Headers $headers
    $links = Invoke-RestMethod "$url/v1/notes/$noteA/links" -Headers $headers
    $download = Join-Path $root 'downloaded.png'; Invoke-WebRequest "$url/v1/assets/$assetId" -Headers $headers -OutFile $download
    if ($search.total -ne 1 -or $links.backlinks.Count -ne 1 -or (Get-FileHash $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $assetHash) { throw 'Restored search, backlinks, or asset validation failed.' }
    [pscustomobject]@{ Result = 'Passed'; Snapshot = 'latest'; Note = $noteA; Asset = $assetId; SearchResults = $search.total; Backlinks = $links.backlinks.Count } | Format-List
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
    $env:RESTIC_REPOSITORY = $oldRepository; $env:RESTIC_PASSWORD = $oldPassword
    if (-not $KeepTemporaryData -and (Test-Path $root)) {
        Remove-DisposableTree $root
    }
}
