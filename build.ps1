param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
$sprite = Join-Path $projectDir 'assets\pet-front.png'
$output = if ($OutputPath) { [System.IO.Path]::GetFullPath($OutputPath) } else { Join-Path $projectDir '大肥鱼桌宠.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows .NET Framework 4.x。' }
if (-not (Test-Path -LiteralPath $sprite)) { throw '缺少 assets\pet-front.png。' }
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'System.Xaml.dll')
$compilerArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', ('/out:' + $output), ('/win32manifest:' + (Join-Path $projectDir 'src\app.manifest')), ('/resource:' + $sprite + ',PetSprite'))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $frameworkDir $reference) }
$animationAssets = @{ 'unified-sheet.png' = 'UnifiedSprites'; 'atlas.json' = 'AnimationAtlas'; 'motion-fields.gz' = 'MotionFields'; 'top-repairs.png' = 'HeadRepairs'; 'top-repairs.json' = 'HeadRepairAtlas' }
foreach ($name in $animationAssets.Keys) {
    $assetPath = Join-Path $projectDir ('assets\animations\v6\' + $name)
    if (-not (Test-Path -LiteralPath $assetPath)) { throw ('缺少动画素材：' + $name) }
    $compilerArgs += '/resource:' + $assetPath + ',' + $animationAssets[$name]
}
$compilerArgs += Join-Path $projectDir 'src\PetApp.cs'
$compilerArgs += Join-Path $projectDir 'src\PetSizeMotion.cs'
$compilerArgs += Join-Path $projectDir 'src\PetAnimation.cs'
$compilerArgs += Join-Path $projectDir 'src\PetMotionWarp.cs'
$compilerArgs += Join-Path $projectDir 'src\AnimationRates.cs'
$compilerArgs += Join-Path $projectDir 'src\NumericSettingRow.cs'
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output ('已生成：' + $output)
