$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll')
$output = Join-Path $PSScriptRoot 'TalkChecks.exe'
$arguments = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + $output))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $frameworkDir $reference) }
$arguments += Join-Path $projectDir 'src\PetTalk.cs'
$arguments += Join-Path $PSScriptRoot 'TalkChecks.cs'
& (Join-Path $frameworkDir 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Talk checks compilation failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Talk checks failed' }
