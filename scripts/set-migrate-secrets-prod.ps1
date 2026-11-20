<#
.SYNOPSIS
  Safely updates PostgreSQL connection string secrets for DB migration Jobs in the production cluster (namespace: avina-workspace) without printing or logging secret values.

.DESCRIPTION
  Updates the 6 migration secrets in namespace avina-workspace:
  - avina-generation-migrate-secret : ConnectionStrings__GenerationDb (DB: etc_avina_generation)
  - avina-billing-migrate-secret    : ConnectionStrings__BillingDb    (DB: etc_avina_billing)
  - avina-users-migrate-secret      : ConnectionStrings__UsersDb      (DB: etc_avina_users)
  - avina-learning-migrate-secret   : ConnectionStrings__LearningDb   (DB: etc_avina_learning)
  - avina-catalog-migrate-secret    : ConnectionStrings__CatalogDb    (DB: etc_avina_catalog)
  - avina-files-migrate-secret      : ConnectionStrings__FilesDb      (DB: etc_avina_files)

  Security features:
  - Fixed context 'kubernetes-admin@kubernetes' and namespace 'avina-workspace'. Does not depend on or alter current kubeconfig context.
  - Verifies that context 'kubernetes-admin@kubernetes' exists in kubeconfig.
  - All kubectl commands are routed through Invoke-Kubectl with explicit --context and -n.
  - Connection strings are read via Read-Host -AsSecureString (masked input).
  - Secret values travel to kubectl exclusively through stdin, never via command-line arguments.
  - Secret labels and metadata are preserved by updating the existing Secret definition and replacing via stdin.
  - Sensitive variables are wiped from memory after use.
  - Pre-flight checks ensure the secrets exist and have the expected keys before prompting.
  - In dry-run mode (default), displays what will be done without prompting or printing values.

.PARAMETER Services
  Array of service names to update. Defaults to all 6 services:
  @('generation', 'billing', 'users', 'learning', 'catalog', 'files').
  Accepts short names (e.g. 'generation'), prefixed names ('avina-generation'), or full secret names.

.PARAMETER Apply
  Actually prompts for connection strings and updates the secrets on the cluster.
  Default behavior (without -Apply) runs in dry-run mode.

.PARAMETER DryRun
  Explicitly performs dry-run checks only (default behavior).

.EXAMPLE
  pwsh scripts/set-migrate-secrets-prod.ps1
  pwsh scripts/set-migrate-secrets-prod.ps1 -Services generation, billing
  pwsh scripts/set-migrate-secrets-prod.ps1 -Apply
  pwsh scripts/set-migrate-secrets-prod.ps1 -Services catalog -Apply
#>
[CmdletBinding()]
param(
    [string[]]$Services = @('generation', 'billing', 'users', 'learning', 'catalog', 'files'),
    [switch]$Apply,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Error $message
    exit 1
}

# Fixed context and namespace for production. Not configurable via parameters.
$Context = 'kubernetes-admin@kubernetes'
$Namespace = 'avina-workspace'

# Safety check: Refuse if context contains 'staging'
if ($Context -match 'staging') {
    Fail "Refusing to use context '$Context': this script is for the production cluster only."
}

function Invoke-Kubectl {
    # Runs kubectl with fixed production context and namespace. Output is returned; on failure only kubectl's own message is shown.
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$KubectlArgs,
        [string]$InputText,
        [switch]$IgnoreError
    )
    $all = @('--context', $Context, '-n', $Namespace) + $KubectlArgs
    if ($PSBoundParameters.ContainsKey('InputText')) {
        $result = $InputText | & kubectl @all 2>&1
    }
    else {
        $result = & kubectl @all 2>&1
    }
    if ($LASTEXITCODE -ne 0 -and -not $IgnoreError) {
        Fail ("kubectl failed: " + (($result | Out-String).Trim()))
    }
    return $result
}

# Verify that the fixed context exists in kubeconfig
$contextList = Invoke-Kubectl @('config', 'get-contexts', '-o', 'name')
$availableContexts = $contextList -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
if ($availableContexts -notcontains $Context) {
    Fail "Context '$Context' not found in kubeconfig. Available contexts: $($availableContexts -join ', ')"
}

# Verify namespace exists in cluster
$null = Invoke-Kubectl @('get', 'namespace', $Namespace, '-o', 'name')

$ServiceConfigs = [ordered]@{
    'generation' = @{
        SecretName = 'avina-generation-migrate-secret'
        KeyName    = 'ConnectionStrings__GenerationDb'
        DbName     = 'etc_avina_generation'
    }
    'billing'    = @{
        SecretName = 'avina-billing-migrate-secret'
        KeyName    = 'ConnectionStrings__BillingDb'
        DbName     = 'etc_avina_billing'
    }
    'users'      = @{
        SecretName = 'avina-users-migrate-secret'
        KeyName    = 'ConnectionStrings__UsersDb'
        DbName     = 'etc_avina_users'
    }
    'learning'   = @{
        SecretName = 'avina-learning-migrate-secret'
        KeyName    = 'ConnectionStrings__LearningDb'
        DbName     = 'etc_avina_learning'
    }
    'catalog'    = @{
        SecretName = 'avina-catalog-migrate-secret'
        KeyName    = 'ConnectionStrings__CatalogDb'
        DbName     = 'etc_avina_catalog'
    }
    'files'      = @{
        SecretName = 'avina-files-migrate-secret'
        KeyName    = 'ConnectionStrings__FilesDb'
        DbName     = 'etc_avina_files'
    }
}

# Normalize selected services
$selectedServiceKeys = [System.Collections.Generic.List[string]]::new()
foreach ($svc in $Services) {
    if ([string]::IsNullOrWhiteSpace($svc)) { continue }
    $normalized = $svc.Trim().ToLowerInvariant()
    $normalized = $normalized -replace '^avina-', ''
    $normalized = $normalized -replace '-migrate-secret$', ''
    if (-not $ServiceConfigs.Contains($normalized)) {
        Fail "Unknown service '$svc'. Allowed services: $($ServiceConfigs.Keys -join ', ')"
    }
    if (-not $selectedServiceKeys.Contains($normalized)) {
        $selectedServiceKeys.Add($normalized)
    }
}

if ($selectedServiceKeys.Count -eq 0) {
    Fail "No valid services selected to process."
}

# Pre-flight check: Verify that each selected secret exists and has the expected key
$secretCache = @{}
foreach ($key in $selectedServiceKeys) {
    $cfg = $ServiceConfigs[$key]
    $secJson = Invoke-Kubectl @('get', 'secret', $cfg.SecretName, '-o', 'json') -IgnoreError
    if ($LASTEXITCODE -ne 0) {
        Fail "Secret '$($cfg.SecretName)' does not exist in namespace '$Namespace'."
    }
    $secObj = $secJson | ConvertFrom-Json
    $dataKeys = if ($secObj.data) { @($secObj.data.PSObject.Properties.Name) } else { @() }
    $stringDataKeys = if ($secObj.stringData) { @($secObj.stringData.PSObject.Properties.Name) } else { @() }
    if (-not ($dataKeys -contains $cfg.KeyName -or $stringDataKeys -contains $cfg.KeyName)) {
        Fail "Secret '$($cfg.SecretName)' exists but does not contain expected key '$($cfg.KeyName)'."
    }
    $secretCache[$key] = $secObj
}

# Dry-run check: Default behavior unless -Apply is explicitly provided.
if (-not $Apply -or $DryRun) {
    Write-Host "DRY RUN (Production) - context '$Context', namespace '$Namespace'"
    Write-Host "  Context check    : verified in kubeconfig ($Context)"
    Write-Host "  Namespace check  : verified in cluster ($Namespace)"
    Write-Host ""
    Write-Host "Services to update ($($selectedServiceKeys.Count) selected):"
    foreach ($key in $selectedServiceKeys) {
        $cfg = $ServiceConfigs[$key]
        Write-Host "  - ${key}:"
        Write-Host "      Secret      : $($cfg.SecretName) (exists)"
        Write-Host "      Key         : $($cfg.KeyName) (key found)"
        Write-Host "      Expected DB : $($cfg.DbName)"
        Write-Host "      Action      : would prompt for connection string via Read-Host -AsSecureString and replace in cluster"
    }
    Write-Host ""
    Write-Host "Pass -Apply to prompt for connection strings and update secrets in production."
    return
}

# --- Apply mode: Prompt operator and update secrets
Write-Host "UPDATING SECRETS (Production) - context '$Context', namespace '$Namespace'"
Write-Host "For each service, enter the connection string (input is masked and never printed)."
Write-Host "Press [Enter] with empty input to skip a service."
Write-Host ""

$updatedSecrets = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($key in $selectedServiceKeys) {
    $cfg = $ServiceConfigs[$key]
    Write-Host "--> Service: $key"
    Write-Host "    Secret      : $($cfg.SecretName)"
    Write-Host "    Key         : $($cfg.KeyName)"
    Write-Host "    Expected DB : $($cfg.DbName)"

    $promptText = "Enter connection string for '$key' (empty to skip)"
    $secStr = Read-Host -Prompt $promptText -AsSecureString

    if ($null -eq $secStr -or $secStr.Length -eq 0) {
        Write-Host "    [SKIPPED] Empty input, secret unchanged.`n"
        continue
    }

    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secStr)
    $plain = $null
    try {
        $plain = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }

    # Validation: Must contain Host=, Database=<DbName>, Username= (or User Id=), Password=, and NOT contain CHANGE_ME
    $missingParts = [System.Collections.Generic.List[string]]::new()
    if ($plain -notmatch 'Host=') { $missingParts.Add('Host=') }
    if ($plain -notmatch "Database=\s*$($cfg.DbName)\b") { $missingParts.Add("Database=$($cfg.DbName)") }
    if ($plain -notmatch 'Username=' -and $plain -notmatch 'User Id=') { $missingParts.Add('Username=') }
    if ($plain -notmatch 'Password=') { $missingParts.Add('Password=') }
    if ($plain -match 'CHANGE_ME') { $missingParts.Add('must not contain CHANGE_ME') }

    if ($missingParts.Count -gt 0) {
        $plain = $null
        $secStr.Dispose()
        [GC]::Collect()
        Fail "Validation failed for service '$key': missing or invalid elements: $($missingParts -join ', '). Secret was NOT updated."
    }

    $valLength = $plain.Length
    $b64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($plain))

    # Retrieve fresh secret to ensure resourceVersion is accurate and labels/metadata are preserved
    $freshJson = Invoke-Kubectl @('get', 'secret', $cfg.SecretName, '-o', 'json')
    $secObj = $freshJson | ConvertFrom-Json

    if ($null -eq $secObj.data) {
        $secObj | Add-Member -NotePropertyName data -NotePropertyValue ([ordered]@{})
    }
    $secObj.data.$($cfg.KeyName) = $b64

    # Send updated Secret JSON through stdin to kubectl replace
    $replaceJson = $secObj | ConvertTo-Json -Depth 10
    $null = Invoke-Kubectl @('replace', '-f', '-') -InputText $replaceJson

    # Wipe memory
    $plain = $null
    $b64 = $null
    $replaceJson = $null
    $freshJson = $null
    $secObj = $null
    $secStr.Dispose()
    [GC]::Collect()

    $updatedSecrets.Add([PSCustomObject]@{
        SecretName  = $cfg.SecretName
        KeyName     = $cfg.KeyName
        ValueLength = $valLength
    })

    Write-Host "    [OK] Secret '$($cfg.SecretName)' updated successfully.`n"
}

Write-Host "=========================================="
Write-Host "Summary of updated secrets:"
if ($updatedSecrets.Count -eq 0) {
    Write-Host "  No secrets were updated (all skipped)."
}
else {
    foreach ($u in $updatedSecrets) {
        Write-Host "  Secret: $($u.SecretName) | Key: $($u.KeyName) | Value length: $($u.ValueLength) characters"
    }
}
Write-Host "=========================================="
