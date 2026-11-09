<#
.SYNOPSIS
  Creates the Kubernetes secrets Shipit needs in the staging cluster, without ever printing a secret value.

.DESCRIPTION
  1. shipit-secrets (namespace devops): database URL (read from the AVINA_STAGING_SHIPIT_DB_URL environment variable),
     a freshly generated master key (AES-256), the admin user and a freshly generated admin password.
     The admin password is written to .secrets/shipit-admin.txt (git-ignored) so you can log in; it is never printed.
  2. registry-secret (namespace devops): a copy of the image pull secret that already exists in avina-workspace.

  Secret values travel through stdin to kubectl, never through its command line.
  The script refuses to replace an existing shipit-secrets: a new master key would make every credential
  stored in the database undecryptable. Use -Force only when you are sure.

.PARAMETER DryRun
  Checks prerequisites and says what would happen. Changes nothing.

.EXAMPLE
  pwsh scripts/create-secrets.ps1 -DryRun
  pwsh scripts/create-secrets.ps1
#>
[CmdletBinding()]
param(
    [string]$Context = 'staging-admin@staging',
    [string]$Namespace = 'devops',
    [string]$SourceNamespace = 'avina-workspace',
    [string]$AdminUser = 'admin',
    [string]$SecretsDir = (Join-Path $PSScriptRoot '..' '.secrets'),
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Error $message
    exit 1
}

# Never run against anything but the staging cluster.
if ($Context -notmatch 'staging') { Fail "Refusing to use context '$Context': this script is for the staging cluster only." }

$dbUrl = [Environment]::GetEnvironmentVariable('AVINA_STAGING_SHIPIT_DB_URL')
if ([string]::IsNullOrWhiteSpace($dbUrl)) { Fail 'The environment variable AVINA_STAGING_SHIPIT_DB_URL is not set in this shell.' }

function Invoke-Kubectl {
    # Runs kubectl against the staging context. Output is returned; on failure only kubectl's own message is shown.
    param([string[]]$KubectlArgs, [string]$InputText)
    $all = @('--context', $Context) + $KubectlArgs
    if ($PSBoundParameters.ContainsKey('InputText')) { $result = $InputText | & kubectl @all 2>&1 }
    else { $result = & kubectl @all 2>&1 }
    if ($LASTEXITCODE -ne 0) { Fail ("kubectl failed: " + (($result | Out-String).Trim())) }
    return $result
}

# Existing state (names only).
$null = Invoke-Kubectl @('get', 'namespace', $Namespace, '-o', 'name')
$existing = & kubectl --context $Context -n $Namespace get secret shipit-secrets -o name 2>$null
$shipitSecretExists = ($LASTEXITCODE -eq 0 -and $existing)
$null = Invoke-Kubectl @('-n', $SourceNamespace, 'get', 'secret', 'registry-secret', '-o', 'name')

if ($shipitSecretExists -and -not $Force) {
    Fail "shipit-secrets already exists in $Namespace. Not replacing it (a new master key would lose stored credentials). Use -Force only if you really want that."
}

if ($DryRun) {
    Write-Host "DRY RUN - context '$Context', namespace '$Namespace'"
    Write-Host '  database URL variable : present (value not shown)'
    Write-Host "  shipit-secrets        : would be $(if ($shipitSecretExists) { 'REPLACED (-Force)' } else { 'created' })"
    Write-Host "  registry-secret       : would be copied from $SourceNamespace"
    Write-Host "  admin password file   : $((Join-Path $SecretsDir 'shipit-admin.txt'))"
    return
}

# --- generate secrets (kept in memory only; never written to the console)
$masterKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$alphabet = (48..57) + (65..90) + (97..122) | ForEach-Object { [char]$_ }
$adminPassword = -join (1..28 | ForEach-Object { $alphabet[[System.Security.Cryptography.RandomNumberGenerator]::GetInt32($alphabet.Count)] })

$secret = @{
    apiVersion = 'v1'
    kind       = 'Secret'
    metadata   = @{ name = 'shipit-secrets'; namespace = $Namespace; labels = @{ 'app.kubernetes.io/name' = 'shipit' } }
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
$passwordFile = Join-Path $SecretsDir 'shipit-admin.txt'
Set-Content -Path $passwordFile -Value "user: $AdminUser`npassword: $adminPassword" -NoNewline -Encoding utf8
try { & icacls $passwordFile /inheritance:r /grant:r "$($env:USERNAME):(R,W)" 2>&1 | Out-Null } catch { }

$null = Invoke-Kubectl @('apply', '-f', '-') -InputText ($secret | ConvertTo-Json -Depth 5)
Write-Host "created shipit-secrets in $Namespace"

# --- copy the image pull secret (read from avina-workspace, written to devops; contents are never displayed)
$source = (Invoke-Kubectl @('-n', $SourceNamespace, 'get', 'secret', 'registry-secret', '-o', 'json') | Out-String) | ConvertFrom-Json
$copy = @{
    apiVersion = 'v1'
    kind       = 'Secret'
    metadata   = @{ name = 'registry-secret'; namespace = $Namespace }
    type       = $source.type
    data       = $source.data
}
$null = Invoke-Kubectl @('apply', '-f', '-') -InputText ($copy | ConvertTo-Json -Depth 8)
Write-Host "copied registry-secret from $SourceNamespace to $Namespace"

Write-Host ''
Write-Host "Admin login saved to: $passwordFile (keep it private; it is git-ignored)"
Write-Host "Names only: $((Invoke-Kubectl @('-n', $Namespace, 'get', 'secret', '-o', 'name')) -join ', ')"
