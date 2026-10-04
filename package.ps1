param([string]$Version='0.2.1')
$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$exe=Join-Path $base 'PixelAtWork.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Run build.ps1 first' }
$items=@('PixelAtWork.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll',
  'artwork.js','overlay.html','overlay.css','overlay.js','启动悬浮条.vbs','config.example.json','README.md','LICENSE','THIRD_PARTY_NOTICES.md','docs/preview.png')
$output=Join-Path $base ('PixelAtWork-Codex-v'+$Version+'-windows-x64.zip')
if(Test-Path -LiteralPath $output){ throw 'Release archive already exists; choose a new version or output' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::Open($output,[IO.Compression.ZipArchiveMode]::Create)
try {
  foreach($item in $items) {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $base $item),$item,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
  $notices=@{
    'licenses/pixel-at-work-MIT.txt'='upstream/LICENSE'
    'licenses/WebView2-LICENSE.txt'='webview2-package/LICENSE.txt'
    'licenses/WebView2-NOTICE.txt'='webview2-package/NOTICE.txt'
  }
  foreach($entry in $notices.GetEnumerator()){
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $base $entry.Value),$entry.Key,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
} finally { $archive.Dispose() }
$hash=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
($hash+'  '+[IO.Path]::GetFileName($output)) | Set-Content -LiteralPath (Join-Path $base 'CHECKSUMS.txt') -Encoding ascii
Get-Item -LiteralPath $output | Select-Object Name,Length
