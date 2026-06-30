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

# 5. Deploy/update the Bing-grounded Foundry agent during provisioning.
$deployAgentOnProvision = ($env:DEPLOY_BING_AGENT_ON_PROVISION ?? 'true').ToLowerInvariant()
if ($deployAgentOnProvision -eq 'true') {
    Write-Host "`n--- Upserting Bing Custom Search configuration ---" -ForegroundColor Cyan

    $bingCliProjectPath = Join-Path $PSScriptRoot '..\tools\AgenticRagScanner.BingCustomSearchCli\AgenticRagScanner.BingCustomSearchCli.csproj'
    $bingConfigurationPath = Join-Path $PSScriptRoot '..\tools\AgenticRagScanner.BingCustomSearchCli\Configuration\bing-custom-search.yaml'

    if (-not $env:BINGCUSTOMSEARCHACCOUNTNAME) {
        Write-Error "Cannot configure Bing Custom Search: BINGCUSTOMSEARCHACCOUNTNAME is not set."
        exit 1
    }

    & dotnet run --project $bingCliProjectPath -- upsert `
        --subscription $env:AZURE_SUBSCRIPTION_ID `
        --resource-group $env:AZURE_RESOURCE_GROUP `
        --bing-account-name $env:BINGCUSTOMSEARCHACCOUNTNAME `
        --bing-configuration-path $bingConfigurationPath `
        --output-format json
    if ($LASTEXITCODE -ne 0) {
        Write-Error "BingCustomSearchCli failed during postprovision (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }

    Write-Host "`n--- Creating Foundry project connection for Bing Custom Search ---" -ForegroundColor Cyan

    if (-not $env:FOUNDRYNAME) {
        Write-Error "Cannot create connection: FOUNDRYNAME is not set."
        exit 1
    }

    if (-not $env:FOUNDRYPROJECTNAME) {
        Write-Error "Cannot create connection: FOUNDRYPROJECTNAME is not set."
        exit 1
    }

    & dotnet run --project $bingCliProjectPath -- create-connection `
        --subscription $env:AZURE_SUBSCRIPTION_ID `
        --resource-group $env:AZURE_RESOURCE_GROUP `
        --bing-account-name $env:BINGCUSTOMSEARCHACCOUNTNAME `
        --foundry-account-name $env:FOUNDRYNAME `
        --foundry-project-name $env:FOUNDRYPROJECTNAME `
        --connection-name "bing-custom-search" `
        --connection-display-name "Bing Custom Search" `
        --output-format json
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create Foundry project connection (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }

    Write-Host "`n--- Deploying Bing-grounded Foundry agent ---" -ForegroundColor Cyan

    $deployCliProjectPath = Join-Path $PSScriptRoot '..\tools\AgenticRagScanner.DeployAgentCli\AgenticRagScanner.DeployAgentCli.csproj'
    $agentYamlPath = Join-Path $PSScriptRoot '..\tools\AgenticRagScanner.DeployAgentCli\Configuration\bing-grounding-agent.yaml'

    $projectEndpoint = $env:FOUNDRY_PROJECT_ENDPOINT
    if (-not $projectEndpoint -and $env:FOUNDRYNAME -and $env:FOUNDRYPROJECTNAME) {
        $projectEndpoint = "https://$($env:FOUNDRYNAME).services.ai.azure.com/api/projects/$($env:FOUNDRYPROJECTNAME)"
        azd env set FOUNDRY_PROJECT_ENDPOINT $projectEndpoint | Out-Null
    }

    if (-not $projectEndpoint) {
        Write-Error "Cannot deploy agent: FOUNDRY_PROJECT_ENDPOINT is not set and could not be derived."
        exit 1
    }

    $bingInstanceName = $env:FOUNDRY_BING_INSTANCE_NAME
    if (-not $bingInstanceName) {
        Write-Error "Cannot deploy agent: set FOUNDRY_BING_INSTANCE_NAME (for example: Jurisdictions)."
        exit 1
    }

    $deployArgs = @(
        'deploy',
        '--endpoint', $projectEndpoint,
        '--yaml-path', $agentYamlPath,
        '--bing-custom-search-instance-name', $bingInstanceName,
        '--output-format', 'json'
    )

    if ($env:FOUNDRY_BING_CONNECTION_ID) {
        $deployArgs += @('--bing-custom-search-connection-id', $env:FOUNDRY_BING_CONNECTION_ID)
    }
    elseif ($env:FOUNDRY_BING_CONNECTION_NAME) {
        $deployArgs += @('--bing-custom-search-connection-name', $env:FOUNDRY_BING_CONNECTION_NAME)
    }
    elseif ($env:BINGCUSTOMSEARCHACCOUNTNAME) {
        # Default to the connection created just above in this script.
        $deployArgs += @('--bing-custom-search-connection-name', 'bing-custom-search')
        azd env set FOUNDRY_BING_CONNECTION_NAME 'bing-custom-search' | Out-Null
    }
    else {
        Write-Error "Cannot deploy agent: set FOUNDRY_BING_CONNECTION_NAME (recommended) or FOUNDRY_BING_CONNECTION_ID."
        exit 1
    }

    & dotnet run --project $deployCliProjectPath -- @deployArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "DeployAgentCli failed during postprovision (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
}

Write-Host "`n=== RBAC setup complete ===" -ForegroundColor Green
