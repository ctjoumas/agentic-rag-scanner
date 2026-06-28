#!/usr/bin/env pwsh
# Post-provision RBAC hook for `azd up` / `azd provision`.
# Reads bicep outputs from azd's environment and calls the RBAC .NET CLI
# to assign the roles required for local development and for the Foundry
# project's managed identity (when AZURE_RBAC_PRINCIPAL_ID is set).

$ErrorActionPreference = 'Stop'

Write-Host "`n=== azd postprovision: RBAC setup ===" -ForegroundColor Cyan

if (-not $env:AZURE_RESOURCE_GROUP) {
    Write-Error "AZURE_RESOURCE_GROUP is not set. Did azd provision succeed?"
    exit 1
}

$projectPath = Join-Path $PSScriptRoot '..\tools\AgenticRagScanner.RbacCli\AgenticRagScanner.RbacCli.csproj'

# Build args from azd-exposed bicep outputs.
$commonArgs = @(
    '--subscription',           $env:AZURE_SUBSCRIPTION_ID,
    '--resource-group',         $env:AZURE_RESOURCE_GROUP,
    '--cosmos-account',         $env:COSMOSACCOUNTNAME,
    '--storage-account',        $env:STORAGEACCOUNTNAME,
    '--foundry-account',        $env:FOUNDRYNAME,
    '--foundry-project',        $env:FOUNDRYPROJECTNAME,
    '--app-config-store',       $env:APPCONFIGSTORENAME,
    '--key-vault',              $env:KEYVAULTNAME,
    '--app-insights',           $env:APPINSIGHTSNAME
)

# 1. Grant Foundry roles to the Foundry account (resource) managed identity first.
$foundryResourceMi = $null
if ($env:FOUNDRYNAME) {
    $foundryScope = "/subscriptions/$($env:AZURE_SUBSCRIPTION_ID)/resourceGroups/$($env:AZURE_RESOURCE_GROUP)/providers/Microsoft.CognitiveServices/accounts/$($env:FOUNDRYNAME)"
    $foundryResourceMi = az cognitiveservices account show --name $env:FOUNDRYNAME --resource-group $env:AZURE_RESOURCE_GROUP --query 'identity.principalId' -o tsv 2>$null
    if ($foundryResourceMi) {
        $foundryResourceMi = $foundryResourceMi.Trim()
        Write-Host "`n--- Granting roles to Foundry resource managed identity ---" -ForegroundColor Cyan
        Write-Host "Foundry resource MI principal ID: $foundryResourceMi" -ForegroundColor DarkGray
        azd env set FOUNDRYRESOURCEPRINCIPALID $foundryResourceMi | Out-Null

        foreach ($role in @('Azure AI Developer', '53ca6127-db72-4b80-b1b0-d745d6d5456d', 'Cognitive Services OpenAI User')) {
            $createResult = az role assignment create --scope $foundryScope --assignee-object-id $foundryResourceMi --assignee-principal-type ServicePrincipal --role $role 2>&1
            if ($LASTEXITCODE -ne 0) {
                $msg = ($createResult | Out-String)
                if ($msg -match 'RoleAssignmentExists') {
                    Write-Host "  [OK] '$role' already assigned on $($env:FOUNDRYNAME)" -ForegroundColor DarkGray
                }
                else {
                    Write-Error "Failed to assign '$role' to Foundry resource MI. $msg"
                    exit 1
                }
            }
            else {
                Write-Host "  [OK] Assigned '$role' on $($env:FOUNDRYNAME)" -ForegroundColor Green
            }
        }
    }
}

# 2. Grant roles to the signed-in user (required for local dev with DefaultAzureCredential).
Write-Host "`n--- Granting roles to signed-in user ---" -ForegroundColor Cyan
& dotnet run --project $projectPath -- @commonArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "RBAC setup for signed-in user failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

# 3. Grant roles to the Foundry project's managed identity when available.
$foundryProjectMi = $env:FOUNDRYPROJECTPRINCIPALID

# Resolve and persist when bicep output is empty.
if (-not $foundryProjectMi -and $env:FOUNDRYPROJECTNAME -and $env:FOUNDRYNAME) {
    $projectResourceId = "/subscriptions/$($env:AZURE_SUBSCRIPTION_ID)/resourceGroups/$($env:AZURE_RESOURCE_GROUP)/providers/Microsoft.CognitiveServices/accounts/$($env:FOUNDRYNAME)/projects/$($env:FOUNDRYPROJECTNAME)"
    $resolvedMi = az resource show --ids $projectResourceId --query 'identity.principalId' -o tsv 2>$null
    if ($resolvedMi) {
        $foundryProjectMi = $resolvedMi.Trim()
        Write-Host "Resolved Foundry project MI principal ID: $foundryProjectMi" -ForegroundColor DarkGray
        azd env set FOUNDRYPROJECTPRINCIPALID $foundryProjectMi | Out-Null
    }
}

if ($foundryProjectMi) {
    Write-Host "`n--- Granting roles to Foundry project managed identity ---" -ForegroundColor Cyan
    & dotnet run --project $projectPath -- @commonArgs `
        --principal-id $foundryProjectMi `
        --principal-name 'Foundry project MI'
    if ($LASTEXITCODE -ne 0) {
        Write-Error "RBAC setup for Foundry project MI failed (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
}

# 4. Optional extra principal (e.g. an app's user-assigned MI) via azd env var.
if ($env:AZURE_RBAC_PRINCIPAL_ID) {
    Write-Host "`n--- Granting roles to $($env:AZURE_RBAC_PRINCIPAL_ID) ---" -ForegroundColor Cyan
    & dotnet run --project $projectPath -- @commonArgs `
        --principal-id $env:AZURE_RBAC_PRINCIPAL_ID `
        --principal-name ($env:AZURE_RBAC_PRINCIPAL_NAME ?? 'azd-configured principal')
    if ($LASTEXITCODE -ne 0) {
        Write-Error "RBAC setup for AZURE_RBAC_PRINCIPAL_ID failed (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
}

Write-Host "`n=== RBAC setup complete ===" -ForegroundColor Green
