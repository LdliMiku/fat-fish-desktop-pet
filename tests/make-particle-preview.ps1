$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$output = Join-Path $PSScriptRoot 'ParticlePreview.exe'
$arguments = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + $output))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $frameworkDir $reference) }
$arguments += '/resource:' + (Join-Path $projectDir 'assets\particles\pink-heart.png') + ',HeartParticle'
$arguments += '/resource:' + (Join-Path $projectDir 'assets\particles\pink-heart-large.png') + ',HeartParticleLarge'
$arguments += Join-Path $projectDir 'src\PetParticles.cs'
$arguments += Join-Path $PSScriptRoot 'ParticlePreview.cs'
& (Join-Path $frameworkDir 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Particle preview compilation failed' }
Push-Location $projectDir
try { & $output '.build\character-hold.png' (Join-Path $PSScriptRoot 'particle-preview.png'); if ($LASTEXITCODE -ne 0) { throw 'Particle preview failed' } }
finally { Pop-Location }
