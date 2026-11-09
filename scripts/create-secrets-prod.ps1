<#
.SYNOPSIS
  Creates the Kubernetes secrets Shipit needs in the production cluster (namespace: cicd), without ever printing a secret value.

.DESCRIPTION
  shipit-secrets (namespace cicd): database URL (read from the AVINA_PROD_SHIPIT_DB_URL environment variable and
  mapped to the AVINA_STAGING_SHIPIT_DB_URL key required by the Shipit application), a freshly generated master key (AES-256),
  the admin user and a freshly generated admin password.
  The admin password is written to .secrets/shipit-admin-prod.txt (git-ignored) so you can log in; it is never printed.

  In production, the container registry (registry.local:5000) does not require authentication and has no imagePullSecret,
  so no registry-secret is copied.

  Secret values travel through stdin to kubectl, never through its command line.
  The script refuses to replace an existing shipit-secrets: a new master key would make every credential
  stored in the database undecryptable.

  By default, the script runs in dry-run mode (pre-flight checks only).
  Pass -Apply to actually create the secret in the cluster.

.PARAMETER Apply
  Actually applies the secret to the production cluster. By default (without -Apply), only checks prerequisites.

.PARAMETER DryRun
  Explicitly performs pre-flight checks and displays what would happen without making changes (default behavior).

.PARAMETER AdminUser
  The initial administrator username (default: 'admin').

.PARAMETER SecretsDir
  The directory to store the generated admin credentials file (default: '.secrets').

.EXAMPLE
  pwsh scripts/create-secrets-prod.ps1
  pwsh scripts/create-secrets-prod.ps1 -Apply
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$DryRun,
    [string]$AdminUser = 'admin',
    [string]$SecretsDir = (Join-Path $PSScriptRoot '..' '.secrets')
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Error $message
    exit 1
}

# Fixed context and namespace for production. Not configurable via parameters.
$Context = 'kubernetes-admin@kubernetes'
$Namespace = 'cicd'

# Safety check: Refuse if context contains 'staging'
if ($Context -match 'staging') {
    Fail "Refusing to use context '$Context': this script is for the production cluster only."
}

# Database URL must be read from AVINA_PROD_SHIPIT_DB_URL
$dbUrl = [Environment]::GetEnvironmentVariable('AVINA_PROD_SHIPIT_DB_URL')
if ([string]::IsNullOrWhiteSpace($dbUrl)) {
    Fail 'The environment variable AVINA_PROD_SHIPIT_DB_URL is not set in this shell.'
}

function Invoke-Kubectl {
    # Runs kubectl against the production context. Output is returned; on failure only kubectl's own message is shown.
    param([string[]]$KubectlArgs, [string]$InputText)
    $all = @('--context', $Context) + $KubectlArgs
    if ($PSBoundParameters.ContainsKey('InputText')) {
        $result = $InputText | & kubectl @all 2>&1
    }
    else {
        $result = & kubectl @all 2>&1
    }
    if ($LASTEXITCODE -ne 0) {
        Fail ("kubectl failed: " + (($result | Out-String).Trim()))
    }
    return $result
}

# Check existing cluster state (names only).
$null = Invoke-Kubectl @('get', 'namespace', $Namespace, '-o', 'name')
$existing = & kubectl --context $Context -n $Namespace get secret shipit-secrets -o name 2>$null
$shipitSecretExists = ($LASTEXITCODE -eq 0 -and $existing)

if ($shipitSecretExists) {
    Fail "shipit-secrets already exists in $Namespace. Refusing to replace it in production (a new master key would lose stored credentials)."
}

# Default behavior: Dry-run check only unless -Apply is explicitly provided.
if (-not $Apply -or $DryRun) {
    Write-Host "DRY RUN (Production) - context '$Context', namespace '$Namespace'"
    Write-Host '  database URL variable (AVINA_PROD_SHIPIT_DB_URL) : present (value not shown)'
    Write-Host '  mapped secret key                                : AVINA_STAGING_SHIPIT_DB_URL'
    Write-Host "  shipit-secrets                                   : would be created"
    Write-Host "  registry-secret                                  : skipped (unauthenticated production registry)"
    Write-Host "  admin password file                              : $((Join-Path $SecretsDir 'shipit-admin-prod.txt'))"
    Write-Host ''
    Write-Host "Pass -Apply to actually create the secret in production."
    return
}

# --- generate secrets (kept in memory only; never written to the console)
$masterKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$alphabet = (48..57) + (65..90) + (97..122) | ForEach-Object { [char]$_ }
$adminPassword = -join (1..28 | ForEach-Object { $alphabet[[System.Security.Cryptography.RandomNumberGenerator]::GetInt32($alphabet.Count)] })

# Note: The application (ShipitOptions / DbUrlParser) reads its database connection string
# from the secret key 'AVINA_STAGING_SHIPIT_DB_URL'. In production, the URL is supplied
# by the operator via environment variable 'AVINA_PROD_SHIPIT_DB_URL' and mapped to
# 'AVINA_STAGING_SHIPIT_DB_URL' in the Kubernetes Secret so the application can read it seamlessly.
$secret = @{
    apiVersion = 'v1'
    kind       = 'Secret'
    metadata   = @{
        name      = 'shipit-secrets'
        namespace = $Namespace
        labels    = @{ 'app.kubernetes.io/name' = 'shipit' }
    }
    type       = 'Opaque'
    stringData = [ordered]@{
        AVINA_STAGING_SHIPIT_DB_URL = $dbUrl
        SHIPIT_MASTER_KEY           = $masterKey
        SHIPIT_ADMIN_USER           = $AdminUser
        SHIPIT_ADMIN_PASSWORD       = $adminPassword
    }
}

# Save the admin password first, so it is never lost if a later step fails.
New-Item -ItemType Directory -Force -Path $SecretsDir | Out-Null
$passwordFile = Join-Path $SecretsDir 'shipit-admin-prod.txt'
Set-Content -Path $passwordFile -Value "user: $AdminUser`npassword: $adminPassword" -NoNewline -Encoding utf8
try { & icacls $passwordFile /inheritance:r /grant:r "$($env:USERNAME):(R,W)" 2>&1 | Out-Null } catch { }

# Secret values travel through stdin to kubectl, never through its command line.
$null = Invoke-Kubectl @('apply', '-f', '-') -InputText ($secret | ConvertTo-Json -Depth 5)
Write-Host "created shipit-secrets in $Namespace"

Write-Host ''
Write-Host "Admin login saved to: $passwordFile (keep it private; it is git-ignored)"
Write-Host "Names only: $((Invoke-Kubectl @('-n', $Namespace, 'get', 'secret', '-o', 'name')) -join ', ')"
