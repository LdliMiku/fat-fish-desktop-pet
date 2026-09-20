$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Security.dll')
$output = Join-Path $PSScriptRoot 'ChatChecks.exe'
$arguments = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + $output))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $frameworkDir $reference) }
$arguments += Join-Path $projectDir 'src\PetChat.cs'
$arguments += Join-Path $PSScriptRoot 'ChatChecks.cs'
& (Join-Path $frameworkDir 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Chat checks compilation failed' }
& $output (Join-Path $PSScriptRoot 'chat-check-runtime')
if ($LASTEXITCODE -ne 0) { throw 'Chat checks failed' }
