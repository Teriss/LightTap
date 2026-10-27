param([switch]$Test, [switch]$LayoutTest, [switch]$CoreTest, [string]$OutputDirectory = 'dist\win-x64')
$ErrorActionPreference = 'Stop'
$env:MSBuildEnableWorkloadResolver = 'false'
$project = Join-Path $PSScriptRoot 'src\KeyKing\KeyKing.csproj'
$output = Join-Path $PSScriptRoot $OutputDirectory
$executable = Join-Path $output 'LightTap.exe'
$obsoleteExecutables = @((Join-Path $output '轻点.exe'), (Join-Path $output 'KeyKing.exe'))
$active = Get-Process -Name @('LightTap', '轻点', 'KeyKing') -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $executable -or $_.Path -in $obsoleteExecutables }
if ($active) { throw 'LightTap 发布版正在运行，请先退出窗口，再重新发布。' }
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $output `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
foreach ($obsoleteExecutable in $obsoleteExecutables) {
    if (Test-Path -LiteralPath $obsoleteExecutable) {
        Remove-Item -LiteralPath $obsoleteExecutable
    }
}
if ($CoreTest) {
    $coreReport = Join-Path $PSScriptRoot 'artifacts\core-checks.json'
    $coreProcess = Start-Process -FilePath $executable `
        -ArgumentList @('--core-test', ('"' + $coreReport + '"')) -WindowStyle Hidden -PassThru
    if (-not $coreProcess.WaitForExit(15000)) { throw '核心逻辑检查超时。' }
    $coreResult = Get-Content -LiteralPath $coreReport -Raw | ConvertFrom-Json
    if ($coreProcess.ExitCode -ne 0 -or -not $coreResult.success) { throw "核心逻辑检查失败，查看 $coreReport" }
    $coreResult.passed
}
if ($LayoutTest) {
    $layoutReport = Join-Path $PSScriptRoot 'artifacts\layout-checks.json'
    $layoutProcess = Start-Process -FilePath $executable `
        -ArgumentList @('--layout-test', ('"' + $layoutReport + '"')) -WindowStyle Hidden -PassThru
    if (-not $layoutProcess.WaitForExit(15000)) { throw '布局检查未能在 15 秒内完成。' }
    $layoutResult = Get-Content -LiteralPath $layoutReport -Raw | ConvertFrom-Json
    if ($layoutProcess.ExitCode -ne 0 -or -not $layoutResult.success) { throw "布局检查失败，查看 $layoutReport" }
    $layoutResult.snapshots | Select-Object name, fits, dpi, width, height
}
if ($Test) {
    $report = Join-Path $PSScriptRoot 'artifacts\integration-checks.json'
    $process = Start-Process -FilePath (Join-Path $output 'LightTap.exe') `
        -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Normal -PassThru
    Write-Host '请在专用测试窗口中点击「运行输入测试」。测试期间请勿操作鼠标键盘。'
    if (-not $process.WaitForExit(120000)) {
        $process.Kill()
        throw '输入测试超时，请检查当前桌面是否允许前台交互。'
    }
    if ($process.ExitCode -ne 0) { throw "输入测试失败，查看 $report" }
    $testResult = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $testResult.success) { throw "输入测试失败，查看 $report" }
    $testResult.passed
}
Get-Item -LiteralPath (Join-Path $output 'LightTap.exe') | Select-Object FullName, Length
