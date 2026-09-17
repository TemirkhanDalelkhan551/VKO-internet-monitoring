$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$coreProjectPath = Join-Path $projectRoot 'src\VkoMonitoring.Agent.Core\VkoMonitoring.Agent.Core.csproj'
$infrastructureProjectPath = Join-Path $projectRoot 'src\VkoMonitoring.Agent.Infrastructure\VkoMonitoring.Agent.Infrastructure.csproj'
$projectPath = Join-Path $projectRoot 'src\VkoMonitoring.Agent\VkoMonitoring.Agent.csproj'
$setupProjectPath = Join-Path $projectRoot 'src\VkoMonitoring.Agent.Setup\VkoMonitoring.Agent.Setup.csproj'
$outputPath = Join-Path $projectRoot 'artifacts\agent-win-x64'

$compilerCandidates = @(
    (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn'),
    (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn'),
    (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin\Roslyn')
)
$compilerPath = $compilerCandidates |
    Where-Object { Test-Path (Join-Path $_ 'csc.exe') } |
    Select-Object -First 1
$compilerArguments = @('-p:UseSharedCompilation=false')

if ($compilerPath) {
    $compilerArguments += "-p:CscToolPath=$compilerPath"
    $compilerArguments += '-p:CscToolExe=csc.exe'
}

foreach ($path in @($coreProjectPath, $infrastructureProjectPath, $projectPath, $setupProjectPath)) {
    $restoreArguments = @(
        'msbuild',
        $path,
        '-t:Restore',
        '-p:RestoreRecursive=false',
        '-p:RestoreIgnoreFailedSources=true',
        '-p:NuGetAudit=false',
        '-p:RuntimeIdentifier=win-x64',
        '-p:SelfContained=true',
        '-m:1',
        '-v:minimal'
    )
    & dotnet @restoreArguments

    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed for $path with exit code $LASTEXITCODE."
    }
}

$publishArguments = @(
    'publish',
    $projectPath,
    '--configuration',
    'Release',
    '--no-restore',
    '--disable-build-servers',
    '-m:1',
    '--runtime',
    'win-x64',
    '--self-contained',
    'true',
    '--output',
    $outputPath
) + $compilerArguments
& dotnet @publishArguments

if ($LASTEXITCODE -ne 0) {
    throw "Agent publishing failed with exit code $LASTEXITCODE."
}

$setupPublishArguments = @(
    'publish',
    $setupProjectPath,
    '--configuration',
    'Release',
    '--no-restore',
    '--disable-build-servers',
    '-m:1',
    '--runtime',
    'win-x64',
    '--self-contained',
    'true',
    '--output',
    $outputPath
) + $compilerArguments
& dotnet @setupPublishArguments

if ($LASTEXITCODE -ne 0) {
    throw "Agent setup publishing failed with exit code $LASTEXITCODE."
}

Write-Host "Agent and setup wizard published to $outputPath"
