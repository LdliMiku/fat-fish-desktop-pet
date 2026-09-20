$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Runtime.Serialization.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')
$arguments = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + (Join-Path $PSScriptRoot 'AnimationChecks.exe')))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $frameworkDir $reference) }
$arguments += '/resource:' + (Join-Path $projectDir 'assets\particles\pink-heart.png') + ',HeartParticle'
$resources = @{ 'unified-sheet.png' = 'UnifiedSprites'; 'atlas.json' = 'AnimationAtlas'; 'top-repairs.png' = 'HeadRepairs'; 'top-repairs.json' = 'HeadRepairAtlas' }
foreach ($name in $resources.Keys) { $arguments += '/resource:' + (Join-Path $projectDir ('assets\animations\v6\' + $name)) + ',' + $resources[$name] }
$arguments += Join-Path $projectDir 'src\PetAnimation.cs'
$arguments += Join-Path $projectDir 'src\PetParticles.cs'
$arguments += Join-Path $projectDir 'src\PetMotionWarp.cs'
$arguments += Join-Path $projectDir 'src\AnimationRates.cs'
$arguments += Join-Path $projectDir 'src\NumericSettingRow.cs'
$motionField = Join-Path $projectDir 'assets\animations\v6\motion-fields.gz'
if (Test-Path -LiteralPath $motionField) { $arguments += '/resource:' + $motionField + ',MotionFields' }
$arguments += Join-Path $PSScriptRoot 'KeyframeChecks.cs'
& (Join-Path $frameworkDir 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Animation checks compilation failed' }
& (Join-Path $PSScriptRoot 'AnimationChecks.exe') @args
if ($LASTEXITCODE -ne 0) { throw 'Animation checks failed' }
