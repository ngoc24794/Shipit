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
    [switch]$NoBuildWeb,
    [switch]$SeedDemo
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
Write-Host "Hướng dẫn chạy thử Job DbMigrator (Fake):" -ForegroundColor Cyan
Write-Host "  1. Đăng nhập tại http://localhost:$Port với tài khoản admin/admin" -ForegroundColor Cyan
Write-Host "  2. Vào 'Allowlist Job' cấu hình: ns 'avina-workspace', configmap 'avina-files-config'," -ForegroundColor Cyan
Write-Host "     'avina-users-config', secret 'avina-files-secret', 'avina-users-secret', pullSecret 'registry-secret'" -ForegroundColor Cyan
Write-Host "  3. Tạo Job 'avina-dbmigrator' (docker/Dockerfile.dbmigrator, context .) với 2 tác vụ:" -ForegroundColor Cyan
Write-Host "     - Tác vụ 1: 'files' (args: migrate --service files)" -ForegroundColor Cyan
Write-Host "     - Tác vụ 2: 'users' (args: migrate --service users)" -ForegroundColor Cyan
Write-Host "  4. Bấm 'Chạy ngay' để xem 4 bước thực thi và log SSE của từng tác vụ." -ForegroundColor Cyan
Write-Host "  Mẹo: Thêm tham số -SeedDemo để tự động khởi tạo sẵn dữ liệu mẫu Job và Pipeline!" -ForegroundColor DarkGray
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
$psi.Environment["SHIPIT_DEPLOY_ALLOWLIST"] = '{"devops":["shipit-demo"],"avina-workspace":["avina-catalog"]}'
$psi.Environment["SHIPIT_JOB_ALLOWLIST"] = '[{"namespace":"avina-workspace","kind":"namespace","name":"avina-workspace"},{"namespace":"avina-workspace","kind":"configmap","name":"avina-files-config"},{"namespace":"avina-workspace","kind":"configmap","name":"avina-users-config"},{"namespace":"avina-workspace","kind":"secret","name":"avina-files-secret"},{"namespace":"avina-workspace","kind":"secret","name":"avina-users-secret"},{"namespace":"avina-workspace","kind":"pullsecret","name":"registry-secret"}]'
$psi.Environment["Shipit__SqlitePath"] = $sqlitePath
$psi.Environment.Remove("AVINA_STAGING_SHIPIT_DB_URL")

$process = [System.Diagnostics.Process]::Start($psi)
if ($null -eq $process) {
    throw "Failed to start dotnet process."
}

# (d) If -SeedDemo is requested, seed resources in background once server is healthy
if ($SeedDemo) {
    [System.Threading.ThreadPool]::QueueUserWorkItem({
        param($state)
        $baseUrl = "http://localhost:" + $state.Port
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        $ready = $false
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 500
            try {
                $resp = Invoke-RestMethod -Uri "$baseUrl/healthz" -Method Get -TimeoutSec 2 -ErrorAction Stop
                if ($resp -eq "ok") {
                    $ready = $true
                    break
                }
            } catch { }
        }

        if ($ready) {
            try {
                $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
                $loginBody = @{ username = "admin"; password = "admin" } | ConvertTo-Json
                Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $loginBody -ContentType "application/json" -WebSession $session | Out-Null

                $existingJobs = Invoke-RestMethod -Uri "$baseUrl/api/jobs" -Method Get -WebSession $session
                if ($existingJobs.Count -eq 0) {
                    $ssh = Invoke-RestMethod -Uri "$baseUrl/api/credentials" -Method Post -Body (@{ name = "demo-ssh-key"; kind = "ssh_key"; value = "-----BEGIN OPENSSH PRIVATE KEY-----`ndemo-key`n-----END OPENSSH PRIVATE KEY-----" } | ConvertTo-Json) -ContentType "application/json" -WebSession $session
                    $git = Invoke-RestMethod -Uri "$baseUrl/api/credentials" -Method Post -Body (@{ name = "demo-git-token"; kind = "git_token"; value = "ghp_demo_token_12345" } | ConvertTo-Json) -ContentType "application/json" -WebSession $session
                    $reg = Invoke-RestMethod -Uri "$baseUrl/api/credentials" -Method Post -Body (@{ name = "demo-registry-pass"; kind = "registry"; username = "ci-bot"; value = "reg_pass_secret_123" } | ConvertTo-Json) -ContentType "application/json" -WebSession $session
                    $slave = Invoke-RestMethod -Uri "$baseUrl/api/slaves" -Method Post -Body (@{ name = "demo-slave-01"; host = "192.168.1.49"; port = 22; user = "devops"; sshCredentialId = $ssh.id } | ConvertTo-Json) -ContentType "application/json" -WebSession $session

                    # Seed Allowlist
                    $entries = @(
                        @{ namespace = "avina-workspace"; kind = "namespace"; name = "avina-workspace" },
                        @{ namespace = "avina-workspace"; kind = "configmap"; name = "avina-files-config" },
                        @{ namespace = "avina-workspace"; kind = "configmap"; name = "avina-users-config" },
                        @{ namespace = "avina-workspace"; kind = "secret"; name = "avina-files-secret" },
                        @{ namespace = "avina-workspace"; kind = "secret"; name = "avina-users-secret" },
                        @{ namespace = "avina-workspace"; kind = "pullsecret"; name = "registry-secret" }
                    )
                    foreach ($e in $entries) {
                        try {
                            Invoke-RestMethod -Uri "$baseUrl/api/job-allow-entries" -Method Post -Body ($e | ConvertTo-Json) -ContentType "application/json" -WebSession $session | Out-Null
                        } catch { }
                    }

                    # Pipeline
                    Invoke-RestMethod -Uri "$baseUrl/api/pipelines" -Method Post -Body (@{
                        name = "demo-pipeline"
                        gitUrl = "https://git.example.com/team/demo.git"
                        gitBranch = "main"
                        gitCredentialId = $git.id
                        slaveId = $slave.id
                        dockerfilePath = "Dockerfile"
                        imageName = "shipit-demo"
                        registryHost = "192.168.1.241"
                        registryCredentialId = $reg.id
                        deployNamespace = "devops"
                        deployDeployment = "shipit-demo"
                        deployContainer = "app"
                    } | ConvertTo-Json) -ContentType "application/json" -WebSession $session | Out-Null

                    # Job DbMigrator
                    Invoke-RestMethod -Uri "$baseUrl/api/jobs" -Method Post -Body (@{
                        name = "avina-dbmigrator"
                        gitUrl = "https://git.example.com/team/avina.git"
                        gitBranch = "main"
                        gitCredentialId = $git.id
                        slaveId = $slave.id
                        dockerfilePath = "docker/Dockerfile.dbmigrator"
                        buildContext = "."
                        imageName = "avina-dbmigrator"
                        registryHost = "192.168.1.241"
                        namespace = "avina-workspace"
                        containerName = "app"
                        imagePullSecrets = @("registry-secret")
                        tasks = @(
                            @{
                                label = "files"
                                args = @("migrate", "--service", "files")
                                envFromConfigMaps = @("avina-files-config")
                                envFromSecrets = @("avina-files-secret")
                                timeoutSeconds = 1800
                                backoffLimit = 0
                                ttlSeconds = 3600
                            },
                            @{
                                label = "users"
                                args = @("migrate", "--service", "users")
                                envFromConfigMaps = @("avina-users-config")
                                envFromSecrets = @("avina-users-secret")
                                timeoutSeconds = 1800
                                backoffLimit = 0
                                ttlSeconds = 3600
                            }
                        )
                    } | ConvertTo-Json -Depth 5) -ContentType "application/json" -WebSession $session | Out-Null

                    [Console]::WriteLine("==> [SeedDemo] Đã khởi tạo dữ liệu mẫu thành công: Pipeline 'demo-pipeline', Job 'avina-dbmigrator'")
                }
            } catch {
                [Console]::WriteLine("==> [SeedDemo] Lỗi khi khởi tạo dữ liệu mẫu: " + $_.Exception.Message)
            }
        }
    }, @{ Port = $Port }) | Out-Null
}

try {
    $process.WaitForExit()
}
finally {
    if (-not $process.HasExited) {
        $process.Kill($true)
    }
}
