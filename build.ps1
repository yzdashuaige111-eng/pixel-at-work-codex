param([switch]$Test)
$ErrorActionPreference = 'Stop'
$base = $PSScriptRoot
& node (Join-Path $base 'build-art.mjs') $base
if ($LASTEXITCODE -ne 0) { throw 'Animation build failed' }
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$lib = Join-Path $base 'webview2-package\lib\net462'
if (!(Test-Path -LiteralPath (Join-Path $lib 'Microsoft.Web.WebView2.Core.dll'))) {
  & node (Join-Path $base 'prepare-sdk.mjs') $base
  if ($LASTEXITCODE -ne 0) { throw 'WebView2 SDK verification failed' }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $base 'webview2.nupkg'),(Join-Path $base 'webview2-package'))
}
Copy-Item -LiteralPath (Join-Path $lib 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $lib 'Microsoft.Web.WebView2.Wpf.dll') -Destination $base -Force
Copy-Item -LiteralPath (Join-Path $base 'webview2-package\runtimes\win-x64\native\WebView2Loader.dll') -Destination $base -Force
$references = @(
  (Join-Path $wpf 'PresentationFramework.dll'), (Join-Path $wpf 'PresentationCore.dll'),
  (Join-Path $wpf 'WindowsBase.dll'), (Join-Path $framework 'System.Xaml.dll'),
  (Join-Path $wpf 'UIAutomationClient.dll'), (Join-Path $wpf 'UIAutomationTypes.dll'),
  (Join-Path $framework 'System.Drawing.dll'), (Join-Path $framework 'System.Windows.Forms.dll'),
  (Join-Path $framework 'System.Web.Extensions.dll'),
  (Join-Path $base 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $base 'Microsoft.Web.WebView2.Wpf.dll')
)
$argsList = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',('/out:'+(Join-Path $base 'PixelAtWork.exe')),('/win32manifest:'+(Join-Path $base 'app.manifest')))
foreach ($reference in $references) { $argsList += '/reference:'+$reference }
$argsList += Join-Path $base 'PixelAtWork.cs'
& (Join-Path $framework 'csc.exe') $argsList
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
if ($Test) {
  $testProcess = Start-Process -FilePath (Join-Path $base 'PixelAtWork.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
  Get-Content -LiteralPath (Join-Path $base 'self-test.txt')
  if ($testProcess.ExitCode -ne 0) { throw 'Session adapter tests failed' }
}
