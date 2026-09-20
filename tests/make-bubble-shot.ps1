$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$output = Join-Path $PSScriptRoot 'BubbleShot.exe'
$arguments = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + $output))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $frameworkDir $reference) }
$arguments += '/resource:' + (Join-Path $projectDir 'assets\ui\bubble-body.png') + ',BubbleBody'
$arguments += '/resource:' + (Join-Path $projectDir 'assets\ui\bubble-tail.png') + ',BubbleTail'
$arguments += '/resource:' + (Join-Path $projectDir 'assets\ui\bubble-whale.png') + ',BubbleWhale'
$arguments += '/resource:' + (Join-Path $projectDir 'assets\ui\bubble-meta.json') + ',BubbleMeta'
$arguments += Join-Path $projectDir 'src\PetBubble.cs'
$arguments += Join-Path $PSScriptRoot 'BubbleShot.cs'
& (Join-Path $frameworkDir 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Bubble shot compilation failed' }
Push-Location $projectDir
try { & $output '呀！' (Join-Path $PSScriptRoot 'bubble-shot.png'); if ($LASTEXITCODE -ne 0) { throw 'Bubble shot failed' } }
finally { Pop-Location }
