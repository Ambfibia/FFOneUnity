param(
    [string]$Player = (Join-Path $PSScriptRoot '../Builds/WorldBenchmark/Player/FFOneWorld.exe'),
    [string]$Output = (Join-Path $PSScriptRoot '../Builds/WorldBenchmark/Unity'),
    [int]$Repeats = 3,
    [switch]$CapturePoses,
    [ValidateSet('default','direct')][string]$GraphicsMode = 'default',
    [ValidateSet('d3d12','vulkan')][string]$Backend = 'vulkan'
)
$ErrorActionPreference = 'Stop'
$taskPlayer = (Resolve-Path -LiteralPath $Player).Path
$taskOutput = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$sites = @(
    @{ Name = 'pool'; Position = '-6374.3188 -56.4746 668.2039' },
    @{ Name = 'city'; Position = '-3750.5798 -56.9 4484.4097' },
    @{ Name = 'campfire'; Position = '-1788.0410 -44.6575 6782.5977' }
)
function Assert-IdleBuilds {
    $building = Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^(rustc|bee_backend)\.exe$' -or
        ($_.Name -eq 'cargo.exe' -and $_.CommandLine -match '\b(build|check|test|release|probe)\b')
    }
    if ($building) { throw 'Compilation is active; wait before taking performance measurements.' }
    $shaderCpu = @{}
    Get-Process -Name UnityShaderCompiler -ErrorAction SilentlyContinue | ForEach-Object { $shaderCpu[$_.Id] = $_.CPU }
    Start-Sleep -Milliseconds 1000
    Get-Process -Name UnityShaderCompiler -ErrorAction SilentlyContinue | ForEach-Object {
        if (!$shaderCpu.ContainsKey($_.Id) -or $_.CPU - $shaderCpu[$_.Id] -gt .03) { throw 'Unity shaders are compiling; wait before measurements.' }
    }
}
function Measure-Site($site, [string]$mode, [string]$name, [string]$step = '', [string]$pose = '') {
    if ($pose -eq '') { Assert-IdleBuilds }
    $folder = Join-Path $taskOutput $name
    if (Test-Path -LiteralPath (Join-Path $folder 'report.json')) { throw ('Refusing to overwrite measured run: ' + $folder) }
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $arguments = @(('-force-' + $Backend), '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0',
        '--world-benchmark', '--fixed-simulation', '--mode', $mode, '--position', ('"' + $site.Position + '"'), '--output', ('"' + $folder + '"'),
        '-logFile', ('"' + (Join-Path $folder 'player.log') + '"'))
    if ($step) { $arguments += @('--step', ('"' + $step + '"')) }
    if ($GraphicsMode -eq 'direct') { $arguments += '-force-gfx-direct' }
    if ($pose -ne '') { $arguments = $arguments | Where-Object { $_ -ne '--fixed-simulation' }; $arguments += @('--pose-seconds', $pose) }
    $compilerCpu = @{}
    Get-Process -Name UnityShaderCompiler -ErrorAction SilentlyContinue | ForEach-Object { $compilerCpu[$_.Id] = $_.CPU }
    $overlap = [Collections.Generic.HashSet[string]]::new()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    # Keep the render window drawable, as in the Rust probe; hiding/minimizing it can suppress presentation.
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $taskPlayer
    $start.WorkingDirectory = (Split-Path -Parent $taskPlayer)
    $start.Arguments = $arguments -join ' '
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (!$process.Start()) { throw ('Could not start world player: ' + $name) }
    $peakWorking = 0L; $peakPrivate = 0L
    while (!$process.HasExited) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($watch.Elapsed.TotalSeconds -gt 300) {
            Stop-Process -Id $process.Id
            throw ('Player timeout: ' + $name)
        }
        if (!$process.HasExited) { $peakWorking = [Math]::Max($peakWorking, $process.WorkingSet64); $peakPrivate = [Math]::Max($peakPrivate, $process.PrivateMemorySize64) }
        Get-Process -Name rustc,bee_backend -ErrorAction SilentlyContinue | ForEach-Object { [void]$overlap.Add($_.ProcessName + ':' + $_.Id) }
        Get-Process -Name UnityShaderCompiler -ErrorAction SilentlyContinue | ForEach-Object {
            if (!$compilerCpu.ContainsKey($_.Id) -or $_.CPU - $compilerCpu[$_.Id] -gt .03) { [void]$overlap.Add($_.ProcessName + ':' + $_.Id) }
        }
    }
    $watch.Stop()
    @{ name = $name; backend = $Backend; graphicsMode = $GraphicsMode; arguments = $arguments; exitCode = $process.ExitCode; processElapsedSeconds = $watch.Elapsed.TotalSeconds;
        peakWorkingSetBytes = $peakWorking; peakPrivateBytes = $peakPrivate; playerSha256 = (Get-FileHash -LiteralPath $taskPlayer -Algorithm SHA256).Hash;
        runtimeSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $taskPlayer) 'FFOneWorld_Data/Managed/FFOne.Runtime.dll') -Algorithm SHA256).Hash;
        bootConfigSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $taskPlayer) 'FFOneWorld_Data/boot.config') -Algorithm SHA256).Hash;
        capturedAt = (Get-Date).ToUniversalTime().ToString('o'); filesystemCache = 'Not flushed';
        overlappingCompilers = @($overlap); perfAccepted = ($pose -eq '' -and $overlap.Count -eq 0 -and $process.ExitCode -eq 0) } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'process.json') -Encoding utf8
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $folder 'report.json'))) { throw ('Player failed: ' + $name + '; inspect player.log/failure.json') }
    if ($overlap.Count -gt 0 -and $pose -eq '') { throw ('Compilation overlapped benchmark: ' + $name + '; timing rejected.') }
    $report = Get-Content -LiteralPath (Join-Path $folder 'report.json') -Raw | ConvertFrom-Json
    Write-Host ('{0}: {1:N1} FPS, ready {2:N3}s, p95 {3:N3}ms' -f $name, $report.fps, $report.readySeconds, $report.p95Ms)
}
if ($CapturePoses) {
    foreach ($site in $sites) { foreach ($pose in @('0','1')) { Measure-Site $site 'shared' ('validation-' + $site.Name + '-shared-frozen-t' + $pose) '' $pose } }
    return
}
for ($repeat = 1; $repeat -le $Repeats; $repeat++) {
    $modes = if ($repeat % 2) { @('shared', 'baseline') } else { @('baseline', 'shared') }
    foreach ($mode in $modes) {
        foreach ($site in $sites) { Measure-Site $site $mode ($site.Name + '-' + $mode + '-' + $repeat) }
    }
}
Measure-Site $sites[0] 'shared' 'pool-transition-shared' '512 0 0'
Measure-Site $sites[0] 'baseline' 'pool-transition-baseline' '512 0 0'
