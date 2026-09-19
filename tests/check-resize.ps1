param([Parameter(Mandatory=$true)][string]$AppPath)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $projectDir '.build/resize-check-runtime'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$checkExe = Join-Path $buildDir 'ResizeWindowChecks.exe'
$arguments = @('/nologo','/target:exe','/codepage:65001',('/out:' + $checkExe))
foreach ($reference in @('System.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')) {
    $arguments += '/reference:' + (Join-Path $frameworkDir $reference)
}
$arguments += Join-Path $PSScriptRoot 'ResizeWindowChecks.cs'
$arguments += Join-Path $PSScriptRoot 'ResizeMotionChecks.cs'
$arguments += Join-Path $projectDir 'src/PetSizeMotion.cs'
& (Join-Path $frameworkDir 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Resize test compilation failed' }
& $checkExe ([System.IO.Path]::GetFullPath($AppPath))
if ($LASTEXITCODE -ne 0) { throw 'Resize window checks failed' }
