$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$studioUrl = 'http://127.0.0.1:8741'
function Test-Studio {
    try {
        $health = Invoke-RestMethod -Uri "$studioUrl/api/health" -TimeoutSec 2
    } catch { return $false }
    if ($health.app -ne 'ffone-studio' -or $health.root -ne $projectRoot) {
        throw "Port 8741 is in use by another application or project."
    }
    return $true
}
if (-not (Test-Studio)) {
    $python = (Get-Command python.exe -ErrorAction Stop).Source
    $logDir = New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Logs/Studio') -Force
    $runId = [guid]::NewGuid().ToString('N')
    $errorLog = Join-Path $logDir "dashboard-$runId.err.log"
    $process = Start-Process -FilePath $python -ArgumentList @('Tools/Studio/dashboard.py', 'serve') -WorkingDirectory $projectRoot -PassThru -WindowStyle Hidden -RedirectStandardError $errorLog -RedirectStandardOutput (Join-Path $logDir "dashboard-$runId.log")
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if (Test-Studio) { $ready = $true; break }
        if ($process.HasExited) { break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw "Could not start FFOne Studio. See $errorLog" }
}
Start-Process $studioUrl
