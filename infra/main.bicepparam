using './main.bicep'

param location = 'centralus'
param foundryLocation = 'eastus2'
param baseName = 'agenticragscanner'
param tags = {
  environment: 'dev'
  managedBy: 'azd'
  project: 'agentic-rag-scanner'
}

param cosmosDatabaseName = 'agentic-rag-scanner-db'
param cosmosContainerName = 'agentic-rag-scanner-container'
param deployFoundryProject = true
param foundryProjectName = 'agentic-rag-scanner-project'
param deployFoundryModelDeployment = true
param foundryModelDeploymentName = 'gpt-5-4'
param foundryModelName = 'gpt-5.4'
param foundryModelVersion = '2026-03-05'
param foundryModelDeploymentSkuName = 'GlobalStandard'
param foundryModelDeploymentSkuCapacity = 1
param deployBingCustomSearch = true
