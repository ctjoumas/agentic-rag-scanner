using './main.bicep'

param location = 'centralus'
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
