[CmdletBinding()]
param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2',
    [string]$DependencyDirectory = (Join-Path $PSScriptRoot '.build\deps'),
    [switch]$SkipRestore,
    [switch]$RunTests
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$managed = Join-Path $GameDirectory 'Risk of Rain 2_Data\Managed'
if (!(Test-Path -LiteralPath (Join-Path $managed 'RoR2.dll'))) { throw 'Set -GameDirectory to a Risk of Rain 2 installation.' }
New-Item -ItemType Directory -Force -Path $DependencyDirectory | Out-Null
$DependencyDirectory = (Resolve-Path -LiteralPath $DependencyDirectory).Path
function Restore-Zip([string]$Name, [string]$Url, [string]$Expected) {
    if (Test-Path -LiteralPath (Join-Path $DependencyDirectory $Expected)) { return }
    if ($SkipRestore) { throw "Missing dependency: $Expected" }
    $archive = Join-Path $DependencyDirectory "$Name.zip"
    Write-Host "Restoring $Name"
    Invoke-WebRequest -Uri $Url -OutFile $archive -TimeoutSec 120 -UseBasicParsing
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $DependencyDirectory $Name) -Force
}
Restore-Zip 'compiler' 'https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers.toolset/4.11.0/microsoft.net.compilers.toolset.4.11.0.nupkg' 'compiler\tasks\net472\csc.exe'
Restore-Zip 'bepinex' 'https://thunderstore.io/package/download/bbepis/BepInExPack/5.4.2122/' 'bepinex\BepInExPack\BepInEx\core\BepInEx.dll'
Restore-Zip 'R2API_Core' 'https://thunderstore.io/package/download/RiskofThunder/R2API_Core/5.3.0/' 'R2API_Core\plugins\R2API.Core\R2API.Core.dll'
Restore-Zip 'R2API_Difficulty' 'https://thunderstore.io/package/download/RiskofThunder/R2API_Difficulty/1.1.2/' 'R2API_Difficulty\plugins\R2API.Difficulty\R2API.Difficulty.dll'
Restore-Zip 'R2API_Language' 'https://thunderstore.io/package/download/RiskofThunder/R2API_Language/1.1.0/' 'R2API_Language\plugins\R2API.Language\R2API.Language.dll'
Restore-Zip 'R2API_Teams' 'https://thunderstore.io/package/download/RiskofThunder/R2API_Teams/1.0.3/' 'R2API_Teams\plugins\R2API.Teams\R2API.Teams.dll'
Restore-Zip 'Risk_Of_Options' 'https://thunderstore.io/package/download/Rune580/Risk_Of_Options/2.8.6/' 'Risk_Of_Options\plugins\RiskOfOptions\RiskOfOptions.dll'
$compiler = Join-Path $DependencyDirectory 'compiler\tasks\net472\csc.exe'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$refs = @(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName })
$refs += @(
    'bepinex\BepInExPack\BepInEx\core\BepInEx.dll',
    'bepinex\BepInExPack\BepInEx\core\0Harmony.dll',
    'R2API_Core\plugins\R2API.Core\R2API.Core.dll',
    'R2API_Difficulty\plugins\R2API.Difficulty\R2API.Difficulty.dll',
    'R2API_Language\plugins\R2API.Language\R2API.Language.dll',
    'R2API_Teams\plugins\R2API.Teams\R2API.Teams.dll',
    'Risk_Of_Options\plugins\RiskOfOptions\RiskOfOptions.dll'
) | ForEach-Object { '/reference:' + (Join-Path $DependencyDirectory $_) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /noconfig /nostdlib+ /target:library /langversion:latest /optimize+ /deterministic+ /debug:portable ("/resource:" + (Join-Path $PSScriptRoot 'assets\arena-mode.png') + ',ArenaPvP.ModeIcon.png') ("/out:" + (Join-Path $dist 'ArenaPvP.dll')) @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
if ($RunTests) {
    $testDirectory = Join-Path $DependencyDirectory 'tests'
    New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
    $testExe = Join-Path $testDirectory 'ArenaRules.Tests.exe'
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    & $compiler /nologo /noconfig /target:exe /langversion:latest ("/reference:" + (Join-Path $framework 'System.dll')) ("/reference:" + (Join-Path $framework 'System.Core.dll')) ("/out:" + $testExe) (Join-Path $PSScriptRoot 'src\ArenaRules.cs') (Join-Path $PSScriptRoot 'src\PickupRules.cs') (Join-Path $PSScriptRoot 'src\BotTacticsRules.cs') (Join-Path $PSScriptRoot 'src\BotBuildRules.cs') (Join-Path $PSScriptRoot 'src\BotActionRules.cs') (Join-Path $PSScriptRoot 'src\BotAdaptation.cs') (Join-Path $PSScriptRoot 'src\BotBattlePlan.cs') (Join-Path $PSScriptRoot 'src\HierarchyWorkQueue.cs') (Join-Path $PSScriptRoot 'src\StartupCallbacks.cs') (Join-Path $PSScriptRoot 'src\DeferredMenuStartup.cs') (Join-Path $PSScriptRoot 'tests\RulesTests.cs') (Join-Path $PSScriptRoot 'tests\BotMotionTests.cs') (Join-Path $PSScriptRoot 'tests\UltraPressureTests.cs') (Join-Path $PSScriptRoot 'src\BotCombatKnowledge.cs') (Join-Path $PSScriptRoot 'src\BotLocomotionFeedback.cs') (Join-Path $PSScriptRoot 'tests\CombatKnowledgeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Rules tests failed.' }
    $poolExe = Join-Path $testDirectory 'ItemPool.Tests.exe'
    & $compiler /nologo /noconfig /target:exe /langversion:latest ("/reference:" + (Join-Path $framework 'System.dll')) ("/reference:" + (Join-Path $framework 'System.Core.dll')) ("/out:" + $poolExe) (Join-Path $PSScriptRoot 'src\ArenaRules.cs') (Join-Path $PSScriptRoot 'src\PickupRules.cs') (Join-Path $PSScriptRoot 'src\BotTacticsRules.cs') (Join-Path $PSScriptRoot 'src\BotBuildRules.cs') (Join-Path $PSScriptRoot 'src\BotActionRules.cs') (Join-Path $PSScriptRoot 'src\BotAdaptation.cs') (Join-Path $PSScriptRoot 'src\BotBattlePlan.cs') (Join-Path $PSScriptRoot 'src\ItemPool.cs') (Join-Path $PSScriptRoot 'src\ArenaBotBuilds.cs') (Join-Path $PSScriptRoot 'tests\ItemPoolTests.cs') (Join-Path $PSScriptRoot 'src\ArenaBotActions.cs') (Join-Path $PSScriptRoot 'tests\BotActionsTests.cs') (Join-Path $PSScriptRoot 'src\BotCombatKnowledge.cs') (Join-Path $PSScriptRoot 'src\ArenaBotAim.cs') (Join-Path $PSScriptRoot 'tests\CombatLearningAdapter.cs') (Join-Path $PSScriptRoot 'tests\BotAimPipelineTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Production ItemPool test compilation failed.' }
    & $poolExe
    if ($LASTEXITCODE -ne 0) { throw 'Production ItemPool tests failed.' }
    $routeExe = Join-Path $testDirectory 'Routes.Tests.exe'
    & $compiler /nologo /noconfig /target:exe /langversion:latest ("/reference:" + (Join-Path $framework 'System.dll')) ("/reference:" + (Join-Path $framework 'System.Core.dll')) ("/out:" + $routeExe) (Join-Path $PSScriptRoot 'src\BotAdaptation.cs') (Join-Path $PSScriptRoot 'src\BotBattlePlan.cs') (Join-Path $PSScriptRoot 'src\ArenaBotRoutes.cs') (Join-Path $PSScriptRoot 'src\ArenaBotPursuit.cs') (Join-Path $PSScriptRoot 'tests\RouteTests.cs') (Join-Path $PSScriptRoot 'src\BotLocomotionFeedback.cs') (Join-Path $PSScriptRoot 'src\ArenaBotLocomotion.cs') (Join-Path $PSScriptRoot 'src\ArenaRules.cs') (Join-Path $PSScriptRoot 'src\PickupRules.cs') (Join-Path $PSScriptRoot 'src\BotTacticsRules.cs') (Join-Path $PSScriptRoot 'src\BotBuildRules.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Production route test compilation failed.' }
    & $routeExe
    if ($LASTEXITCODE -ne 0) { throw 'Production graph-route tests failed.' }
    $catalogExe = Join-Path $testDirectory 'CombatCatalog.Tests.exe'
    & $compiler /nologo /noconfig /target:exe /langversion:latest ("/reference:" + (Join-Path $framework 'System.dll')) ("/reference:" + (Join-Path $framework 'System.Core.dll')) ("/out:" + $catalogExe) (Join-Path $PSScriptRoot 'src\BotCombatKnowledge.cs') (Join-Path $PSScriptRoot 'src\ArenaCombatLearning.cs') (Join-Path $PSScriptRoot 'tests\CombatCatalogTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Combat catalog test compilation failed.' }
    & $catalogExe
    if ($LASTEXITCODE -ne 0) { throw 'Production combat catalog tests failed.' }
    $bepinex = Join-Path $DependencyDirectory 'bepinex\BepInExPack\BepInEx\core\BepInEx.dll'
    Copy-Item -LiteralPath $bepinex -Destination (Join-Path $testDirectory 'BepInEx.dll') -Force
    $migrationExe = Join-Path $testDirectory 'Settings.Tests.exe'
    # This BepInEx distribution carries a 32-bit-required CLR flag; run its
    # standalone config tests in x86 CLR rather than altering the dependency.
    & $compiler /nologo /noconfig /target:exe /platform:x86 /langversion:latest ("/reference:" + (Join-Path $framework 'System.dll')) ("/reference:" + (Join-Path $framework 'System.Core.dll')) ("/reference:" + $bepinex) ("/reference:" + (Join-Path $managed 'UnityEngine.dll')) ("/reference:" + (Join-Path $managed 'UnityEngine.CoreModule.dll')) ("/out:" + $migrationExe) (Join-Path $PSScriptRoot 'src\ArenaRules.cs') (Join-Path $PSScriptRoot 'src\PickupRules.cs') (Join-Path $PSScriptRoot 'src\BotTacticsRules.cs') (Join-Path $PSScriptRoot 'src\BotBuildRules.cs') (Join-Path $PSScriptRoot 'src\ArenaConfigMigration.cs') (Join-Path $PSScriptRoot 'src\SettingsSnapshotCodec.cs') (Join-Path $PSScriptRoot 'src\MatchSnapshotCodec.cs') (Join-Path $PSScriptRoot 'src\ArenaConfig.cs') (Join-Path $PSScriptRoot 'src\ArenaModeConfig.cs') (Join-Path $PSScriptRoot 'tests\SettingsUiStubs.cs') (Join-Path $PSScriptRoot 'tests\SettingsTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Settings test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $managed 'UnityEngine.dll'), (Join-Path $managed 'UnityEngine.CoreModule.dll') -Destination $testDirectory -Force
    & $migrationExe
    if ($LASTEXITCODE -ne 0) { throw 'Settings migration/snapshot tests failed.' }
    $cecil = Join-Path $DependencyDirectory 'bepinex\BepInExPack\BepInEx\core\Mono.Cecil.dll'
    Copy-Item -LiteralPath $cecil -Destination (Join-Path $testDirectory 'Mono.Cecil.dll') -Force
    $auditExe = Join-Path $testDirectory 'ApiAudit.exe'
    & $compiler /nologo /noconfig /target:exe /langversion:latest ("/reference:" + (Join-Path $framework 'System.Core.dll')) ("/reference:" + $cecil) ("/out:" + $auditExe) (Join-Path $PSScriptRoot 'tests\ApiAudit.cs')
    if ($LASTEXITCODE -ne 0) { throw 'API audit compilation failed.' }
    & $auditExe (Join-Path $managed 'RoR2.dll') (Join-Path $dist 'ArenaPvP.dll')
    if ($LASTEXITCODE -ne 0) { throw 'API audit failed.' }
}
Write-Host "Built $(Join-Path $dist 'ArenaPvP.dll')"
