@description('Azure region for Azure AI Foundry account.')
param location string

@description('Microsoft Foundry account name.')
param accountName string

@description('Deploy a Foundry project under the account.')
param deployProject bool = false

@description('Foundry project name to create when deployProject is true.')
param projectName string = ''

@description('Deploy an OpenAI model deployment under the Foundry account.')
param deployModelDeployment bool = false

@description('Model deployment name under the Foundry account.')
param modelDeploymentName string = ''

@description('OpenAI model name to deploy (for example: gpt-5.4).')
param modelName string = 'gpt-5.4'

@description('OpenAI model version to deploy (for example: 2025-06-01).')
param modelVersion string = ''

@description('SKU name for the model deployment.')
param modelDeploymentSkuName string = 'GlobalStandard'

@description('SKU capacity for the model deployment.')
@minValue(1)
param modelDeploymentSkuCapacity int = 1

@description('Tags applied to Foundry resources.')
param tags object = {}

var deployedModel = empty(modelVersion)
  ? {
      format: 'OpenAI'
      name: modelName
    }
  : {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
  kind: 'AIServices'
  tags: tags
  properties: {
    allowProjectManagement: true
    customSubDomainName: accountName
    publicNetworkAccess: 'Enabled'
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = if (deployProject) {
  name: projectName
  parent: foundryAccount
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  tags: tags
  properties: {}
}

resource foundryModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = if (deployModelDeployment) {
  name: modelDeploymentName
  parent: foundryAccount
  sku: {
    name: modelDeploymentSkuName
    capacity: modelDeploymentSkuCapacity
  }
  properties: {
    model: deployedModel
  }
}

output foundryName string = foundryAccount.name
output foundryEndpoint string = foundryAccount.properties.endpoint
output foundryProjectName string = deployProject ? foundryProject!.name : ''
output foundryProjectPrincipalId string = deployProject ? foundryProject!.identity!.principalId : ''
output foundryModelDeploymentName string = deployModelDeployment ? foundryModelDeployment.name : ''
