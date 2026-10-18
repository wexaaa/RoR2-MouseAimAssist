param(
    [string]$GamePath = "D:\steam\steamapps\common\Risk of Rain 2",
    [string]$BepInExCore,
    [string]$RiskOfOptionsDll
)

$ErrorActionPreference = "Stop"
$managed = Join-Path $GamePath "Risk of Rain 2_Data\Managed"
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not $BepInExCore) {
    throw "Передайте -BepInExCore с путем к папке BepInEx/core."
}
if (-not $RiskOfOptionsDll) {
    throw "Передайте -RiskOfOptionsDll с путем к RiskOfOptions.dll."
}

$output = Join-Path $PSScriptRoot "bin\MouseAimAssist.dll"
New-Item -ItemType Directory -Force -Path (Split-Path $output) | Out-Null

$references = @(
    (Join-Path $managed "mscorlib.dll"),
    (Join-Path $managed "netstandard.dll"),
    (Join-Path $managed "System.dll"),
    (Join-Path $managed "System.Core.dll"),
    (Join-Path $managed "RoR2.dll"),
    (Join-Path $managed "com.unity.multiplayer-hlapi.Runtime.dll"),
    (Join-Path $managed "HGCSharpUtils.dll"),
    (Join-Path $managed "HGUnityUtils.dll"),
    (Join-Path $managed "UnityEngine.dll"),
    (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.InputLegacyModule.dll"),
    (Join-Path $managed "UnityEngine.IMGUIModule.dll"),
    (Join-Path $managed "UnityEngine.PhysicsModule.dll"),
    (Join-Path $managed "UnityEngine.TextRenderingModule.dll"),
    (Join-Path $BepInExCore "BepInEx.dll"),
    (Join-Path $BepInExCore "0Harmony.dll"),
    $RiskOfOptionsDll
)

$referenceArgument = "/reference:" + ($references -join ",")
& $compiler /noconfig /nologo /target:library /optimize+ /nostdlib+ /out:$output $referenceArgument (Join-Path $PSScriptRoot "MouseAimAssist.cs")
if ($LASTEXITCODE -ne 0) { throw "Сборка завершилась с ошибкой." }

Write-Host "Готово: $output"
