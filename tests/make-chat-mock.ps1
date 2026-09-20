$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$output = Join-Path $PSScriptRoot 'ChatWindowMock.exe'
$arguments = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + $output))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $frameworkDir $reference) }
$arguments += '/resource:' + (Join-Path $projectDir 'assets\ui\bubble-body.png') + ',BubbleBody'
$arguments += '/resource:' + (Join-Path $projectDir 'assets\ui\bubble-tail.png') + ',BubbleTail'
$arguments += '/resource:' + (Join-Path $projectDir 'assets\ui\bubble-whale.png') + ',BubbleWhale'
$arguments += '/resource:' + (Join-Path $projectDir 'assets\ui\bubble-meta.json') + ',BubbleMeta'
$arguments += Join-Path $projectDir 'src\PetBubble.cs'
$arguments += Join-Path $PSScriptRoot 'ChatWindowMock.cs'
& (Join-Path $frameworkDir 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Chat mock compilation failed' }
Push-Location $PSScriptRoot
try { & $output (Join-Path $PSScriptRoot 'chat-mock.png'); if ($LASTEXITCODE -ne 0) { throw 'Chat mock failed' } }
finally { Pop-Location }
