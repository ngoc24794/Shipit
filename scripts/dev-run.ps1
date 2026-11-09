#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs Shipit locally in fake mode for manual exploration and development.
.PARAMETER Port
    HTTP port to bind (default: 5000).
.PARAMETER NoBuildWeb
    Skip building the frontend SPA into src/Shipit.Api/wwwroot.
#>
[CmdletBinding()]
param(
    [int]$Port = 5000,
    [switch]$NoBuildWeb
)

$ErrorActionPreference = 'Stop'

# Ensure AVINA_STAGING_SHIPIT_DB_URL is stripped from current session as well
Remove-Item Env:\AVINA_STAGING_SHIPIT_DB_URL -ErrorAction SilentlyContinue

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# (a) Build web frontend if requested and package.json is present
$webDir = Join-Path $repoRoot "web"
$packageJson = Join-Path $webDir "package.json"

if (-not $NoBuildWeb -and (Test-Path $packageJson)) {
    Write-Host "==> Checking and building web frontend..." -ForegroundColor Cyan
    Push-Location $webDir
    try {
        $nodeModules = Join-Path $webDir "node_modules"
        if (-not (Test-Path $nodeModules)) {
            Write-Host "==> Running 'npm ci'..." -ForegroundColor Cyan
            npm ci
            if ($LASTEXITCODE -ne 0) {
                throw "npm ci failed with exit code $LASTEXITCODE"
            }
        }
        Write-Host "==> Running 'npm run build'..." -ForegroundColor Cyan
        npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "npm run build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

# (b) Prepare local SQLite database directory (.dev/)
$devDir = Join-Path $repoRoot ".dev"
if (-not (Test-Path $devDir)) {
    New-Item -ItemType Directory -Path $devDir -Force | Out-Null
}
$sqlitePath = Join-Path $devDir "shipit-dev.db"

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  Shipit - Local Fake Server" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  URL:              http://localhost:$Port" -ForegroundColor Yellow
Write-Host "  Admin Username:   admin" -ForegroundColor Yellow
Write-Host "  Admin Password:   admin" -ForegroundColor Yellow
Write-Host "  Database (fake):  $sqlitePath" -ForegroundColor Gray
Write-Host "  Mode:             Shipit:UseFakes=true (isolated local DB)" -ForegroundColor Gray
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Press Ctrl+C to stop the server." -ForegroundColor Gray
Write-Host ""

# (c) Launch dotnet run with environment variables configured ONLY for the child process
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "dotnet"
$psi.Arguments = "run --project `"$repoRoot/src/Shipit.Api`" --urls http://localhost:$Port"
$psi.WorkingDirectory = $repoRoot
$psi.UseShellExecute = $false

# Set required fake variables and strip AVINA_STAGING_SHIPIT_DB_URL from child process environment
$psi.Environment["Shipit__UseFakes"] = "true"
$psi.Environment["SHIPIT_ADMIN_USER"] = "admin"
$psi.Environment["SHIPIT_ADMIN_PASSWORD"] = "admin"
$psi.Environment["SHIPIT_DEPLOY_ALLOWLIST"] = '{"devops":["shipit-demo"]}'
$psi.Environment["Shipit__SqlitePath"] = $sqlitePath
$psi.Environment.Remove("AVINA_STAGING_SHIPIT_DB_URL")

$process = [System.Diagnostics.Process]::Start($psi)
if ($null -eq $process) {
    throw "Failed to start dotnet process."
}

try {
    $process.WaitForExit()
}
finally {
    if (-not $process.HasExited) {
        $process.Kill($true)
    }
}
