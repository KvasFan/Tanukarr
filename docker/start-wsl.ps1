[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$Distribution = $(if ($env:TANUKARR_WSL_DISTRIBUTION) { $env:TANUKARR_WSL_DISTRIBUTION } else { 'SonarrWeebBuild' }),
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$stateDirectory = Join-Path $env:LOCALAPPDATA 'SonarrWeebTools\docker-bootstrap'
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$statePath = Join-Path $stateDirectory ('keepalive-' + $Distribution + '.json')
$wslExecutable = Join-Path $env:SystemRoot 'System32\wsl.exe'
$keepalive = $null

if (Test-Path -LiteralPath $statePath) {
    try {
        $saved = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        $candidate = Get-Process -Id ([int]$saved.processId) -ErrorAction Stop
        if ($candidate.Path -eq $wslExecutable -and
            $candidate.StartTime.ToUniversalTime().Ticks -eq ([DateTime]$saved.startTime).ToUniversalTime().Ticks) {
            $keepalive = $candidate
        }
    } catch {
        # A closed or obsolete session is replaced without touching other processes.
    }
}

if (!$keepalive) {
    # WSL systemd services alone do not keep the distribution running. This
    # foreground Linux session stays hidden and preserves the local deployment.
    $keepalive = Start-Process -FilePath $wslExecutable -WindowStyle Hidden -PassThru `
        -ArgumentList @('-d', $Distribution, '--exec', 'sleep', 'infinity') `
        -RedirectStandardOutput (Join-Path $stateDirectory ('keepalive-' + $Distribution + '.stdout.log')) `
        -RedirectStandardError (Join-Path $stateDirectory ('keepalive-' + $Distribution + '.stderr.log'))
    @{processId=$keepalive.Id;startTime=$keepalive.StartTime.ToUniversalTime().ToString('o');distribution=$Distribution} |
        ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
}

$engineReady = $false
for ($attempt=0; $attempt -lt 40; $attempt++) {
    $keepalive.Refresh()
    if ($keepalive.HasExited) { throw 'The WSL session exited; check the keepalive stderr log' }
    & $wslExecutable -d $Distribution --exec docker info --format '{{.ServerVersion}}' *> $null
    if ($LASTEXITCODE -eq 0) { $engineReady=$true; break }
    Start-Sleep -Milliseconds 500
}
if (!$engineReady) { throw 'Docker Engine did not become ready in the selected distribution' }

$linuxProject = (& $wslExecutable -d $Distribution --exec wslpath -u $projectPath).Trim()
if ($LASTEXITCODE -ne 0 -or !$linuxProject) { throw 'Could not resolve the repository path inside WSL' }
if ($Build) {
    & $wslExecutable -d $Distribution --cd $linuxProject --exec docker compose build
    if ($LASTEXITCODE -ne 0) { throw 'Docker image build failed' }
}
& $wslExecutable -d $Distribution --cd $linuxProject --exec docker compose up -d --no-build
if ($LASTEXITCODE -ne 0) { throw 'Compose deployment failed' }
& $wslExecutable -d $Distribution --cd $linuxProject --exec docker compose ps
if ($LASTEXITCODE -ne 0) { throw 'Could not read Compose deployment status' }
Write-Output ('Hidden WSL session: ' + $keepalive.Id + '; state: ' + $statePath)
