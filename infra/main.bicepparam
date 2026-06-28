using './main.bicep'

param location = 'centralus'
param baseName = 'agenticragscanner'
param tags = {
  environment: 'dev'
  managedBy: 'azd'
  project: 'agentic-rag-scanner'
}

param cosmosDatabaseName = 'agentic-rag-scanner'
param cosmosContainerName = 'results'
param deployFoundryProject = false
param foundryProjectName = 'default'
