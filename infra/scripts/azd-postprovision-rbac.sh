#!/usr/bin/env sh
# Post-provision RBAC hook for `azd up` / `azd provision`.
# Reads bicep outputs from azd's environment and calls the RBAC .NET CLI
# to assign the roles required for local development and for the Foundry
# project's managed identity (when AZURE_RBAC_PRINCIPAL_ID is set).

set -e

echo ""
echo "=== azd postprovision: RBAC setup ==="

if [ -z "${AZURE_RESOURCE_GROUP:-}" ]; then
    echo "ERROR: AZURE_RESOURCE_GROUP is not set. Did azd provision succeed?" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_PATH="$SCRIPT_DIR/../tools/AgenticRagScanner.RbacCli/AgenticRagScanner.RbacCli.csproj"

run_setup() {
    dotnet run --project "$PROJECT_PATH" -- \
        --subscription           "$AZURE_SUBSCRIPTION_ID" \
        --resource-group         "$AZURE_RESOURCE_GROUP" \
        --cosmos-account         "$COSMOSACCOUNTNAME" \
        --storage-account        "$STORAGEACCOUNTNAME" \
        --foundry-account        "$FOUNDRYNAME" \
        --foundry-project        "$FOUNDRYPROJECTNAME" \
        --app-config-store       "$APPCONFIGSTORENAME" \
        --key-vault              "$KEYVAULTNAME" \
        --app-insights           "$APPINSIGHTSNAME" \
        "$@"
}

    # 1. Grant Foundry roles to the Foundry account (resource) managed identity first.
if [ -n "${FOUNDRYNAME:-}" ]; then
    FOUNDRY_SCOPE="/subscriptions/${AZURE_SUBSCRIPTION_ID}/resourceGroups/${AZURE_RESOURCE_GROUP}/providers/Microsoft.CognitiveServices/accounts/${FOUNDRYNAME}"
    FOUNDRY_RESOURCE_MI="$(az cognitiveservices account show --name "$FOUNDRYNAME" --resource-group "$AZURE_RESOURCE_GROUP" --query identity.principalId -o tsv 2>/dev/null || true)"

    if [ -n "$FOUNDRY_RESOURCE_MI" ]; then
        echo ""
        echo "--- Granting roles to Foundry resource managed identity ---"
        azd env set FOUNDRYRESOURCEPRINCIPALID "$FOUNDRY_RESOURCE_MI" >/dev/null

        for ROLE in "Azure AI Developer" "53ca6127-db72-4b80-b1b0-d745d6d5456d" "Cognitive Services OpenAI User"; do
            set +e
            OUTPUT="$(az role assignment create --scope "$FOUNDRY_SCOPE" --assignee-object-id "$FOUNDRY_RESOURCE_MI" --assignee-principal-type ServicePrincipal --role "$ROLE" 2>&1)"
            EXIT_CODE=$?
            set -e

            if [ "$EXIT_CODE" -ne 0 ]; then
                if echo "$OUTPUT" | grep -q "RoleAssignmentExists"; then
                    echo "  [OK] '$ROLE' already assigned on $FOUNDRYNAME"
                else
                    echo "ERROR: Failed to assign '$ROLE' to Foundry resource MI. $OUTPUT" >&2
                    exit 1
                fi
            else
                echo "  [OK] Assigned '$ROLE' on $FOUNDRYNAME"
            fi
        done
    fi
fi

# 2. Signed-in user (required for local dev with DefaultAzureCredential).
echo ""
echo "--- Granting roles to signed-in user ---"
run_setup

# 3. Foundry project's managed identity.
FOUNDRY_PROJECT_MI="${FOUNDRYPROJECTPRINCIPALID:-}"

# Resolve and persist when bicep output is empty.
if [ -z "$FOUNDRY_PROJECT_MI" ] && [ -n "${FOUNDRYPROJECTNAME:-}" ] && [ -n "${FOUNDRYNAME:-}" ]; then
    PROJECT_RESOURCE_ID="/subscriptions/${AZURE_SUBSCRIPTION_ID}/resourceGroups/${AZURE_RESOURCE_GROUP}/providers/Microsoft.CognitiveServices/accounts/${FOUNDRYNAME}/projects/${FOUNDRYPROJECTNAME}"
    FOUNDRY_PROJECT_MI="$(az resource show --ids "$PROJECT_RESOURCE_ID" --query identity.principalId -o tsv 2>/dev/null || true)"
    if [ -n "$FOUNDRY_PROJECT_MI" ]; then
        azd env set FOUNDRYPROJECTPRINCIPALID "$FOUNDRY_PROJECT_MI" >/dev/null
    fi
fi

if [ -n "$FOUNDRY_PROJECT_MI" ]; then
    echo ""
    echo "--- Granting roles to Foundry project managed identity ---"
    run_setup --principal-id "$FOUNDRY_PROJECT_MI" --principal-name "Foundry project MI"
fi

# 4. Optional extra principal via azd env var.
if [ -n "${AZURE_RBAC_PRINCIPAL_ID:-}" ]; then
    NAME="${AZURE_RBAC_PRINCIPAL_NAME:-azd-configured principal}"
    echo ""
    echo "--- Granting roles to $AZURE_RBAC_PRINCIPAL_ID ---"
    run_setup --principal-id "$AZURE_RBAC_PRINCIPAL_ID" --principal-name "$NAME"
fi

# 5. Deploy/update the Bing-grounded Foundry agent during provisioning.
DEPLOY_BING_AGENT_ON_PROVISION_LOWER="$(printf '%s' "${DEPLOY_BING_AGENT_ON_PROVISION:-true}" | tr '[:upper:]' '[:lower:]')"
if [ "$DEPLOY_BING_AGENT_ON_PROVISION_LOWER" = "true" ]; then
    echo ""
    echo "--- Upserting Bing Custom Search configuration ---"

    BING_CLI_PROJECT="$SCRIPT_DIR/../tools/AgenticRagScanner.BingCustomSearchCli/AgenticRagScanner.BingCustomSearchCli.csproj"
    BING_CONFIGURATION_PATH="$SCRIPT_DIR/../tools/AgenticRagScanner.BingCustomSearchCli/Configuration/bing-custom-search.yaml"

    if [ -z "${BINGCUSTOMSEARCHACCOUNTNAME:-}" ]; then
        echo "ERROR: Cannot configure Bing Custom Search: BINGCUSTOMSEARCHACCOUNTNAME is not set." >&2
        exit 1
    fi

    dotnet run --project "$BING_CLI_PROJECT" -- upsert \
        --subscription "$AZURE_SUBSCRIPTION_ID" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --bing-account-name "$BINGCUSTOMSEARCHACCOUNTNAME" \
        --bing-configuration-path "$BING_CONFIGURATION_PATH" \
        --output-format json
    
    echo ""
    echo "--- Deploying Bing-grounded Foundry agent ---"

    DEPLOY_CLI_PROJECT="$SCRIPT_DIR/../tools/AgenticRagScanner.DeployAgentCli/AgenticRagScanner.DeployAgentCli.csproj"
    AGENT_YAML_PATH="$SCRIPT_DIR/../tools/AgenticRagScanner.DeployAgentCli/Configuration/bing-grounding-agent.yaml"

    PROJECT_ENDPOINT="${FOUNDRY_PROJECT_ENDPOINT:-}"
    if [ -z "$PROJECT_ENDPOINT" ] && [ -n "${FOUNDRYNAME:-}" ] && [ -n "${FOUNDRYPROJECTNAME:-}" ]; then
        PROJECT_ENDPOINT="https://${FOUNDRYNAME}.services.ai.azure.com/api/projects/${FOUNDRYPROJECTNAME}"
        azd env set FOUNDRY_PROJECT_ENDPOINT "$PROJECT_ENDPOINT" >/dev/null
    fi

    if [ -z "$PROJECT_ENDPOINT" ]; then
        echo "ERROR: Cannot deploy agent: FOUNDRY_PROJECT_ENDPOINT is not set and could not be derived." >&2
        exit 1
    fi

    if [ -z "${FOUNDRY_BING_INSTANCE_NAME:-}" ]; then
        echo "ERROR: Cannot deploy agent: set FOUNDRY_BING_INSTANCE_NAME (for example: Jurisdictions)." >&2
        exit 1
    fi

    DEPLOY_ARGS="deploy --endpoint \"$PROJECT_ENDPOINT\" --yaml-path \"$AGENT_YAML_PATH\" --bing-custom-search-instance-name \"$FOUNDRY_BING_INSTANCE_NAME\" --output-format json"

    if [ -n "${FOUNDRY_BING_CONNECTION_ID:-}" ]; then
        DEPLOY_ARGS="$DEPLOY_ARGS --bing-custom-search-connection-id \"$FOUNDRY_BING_CONNECTION_ID\""
    elif [ -n "${FOUNDRY_BING_CONNECTION_NAME:-}" ]; then
        DEPLOY_ARGS="$DEPLOY_ARGS --bing-custom-search-connection-name \"$FOUNDRY_BING_CONNECTION_NAME\""
    else
        echo "ERROR: Cannot deploy agent: set FOUNDRY_BING_CONNECTION_NAME (recommended) or FOUNDRY_BING_CONNECTION_ID." >&2
        exit 1
    fi

    # shellcheck disable=SC2086
    eval dotnet run --project "$DEPLOY_CLI_PROJECT" -- $DEPLOY_ARGS
fi

echo ""
echo "=== RBAC setup complete ==="
