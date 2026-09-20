$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$output = Join-Path $PSScriptRoot 'BubbleChecks.exe'
$arguments = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + $output))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $frameworkDir $reference) }
$uiAssets = @{ 'bubble-body.png' = 'BubbleBody'; 'bubble-tail.png' = 'BubbleTail'; 'bubble-whale.png' = 'BubbleWhale' }
foreach ($name in $uiAssets.Keys) { $arguments += '/resource:' + (Join-Path $projectDir ('assets\ui\' + $name)) + ',' + $uiAssets[$name] }
$arguments += '/resource:' + (Join-Path $projectDir 'assets\ui\bubble-meta.json') + ',BubbleMeta'
$arguments += '/reference:' + (Join-Path $frameworkDir 'System.Web.Extensions.dll')
$arguments += Join-Path $projectDir 'src\PetBubble.cs'
$arguments += Join-Path $PSScriptRoot 'BubbleChecks.cs'
& (Join-Path $frameworkDir 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Bubble checks compilation failed' }
Push-Location $projectDir
try {
    & $output (Join-Path $PSScriptRoot 'bubble-preview.png')
    if ($LASTEXITCODE -ne 0) { throw 'Bubble checks failed' }
} finally { Pop-Location }

