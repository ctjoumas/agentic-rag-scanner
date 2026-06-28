@description('Azure region for Azure AI Foundry account.')
param location string

@description('Microsoft Foundry account name.')
param accountName string

@description('Deploy a Foundry project under the account.')
param deployProject bool = false

@description('Foundry project name to create when deployProject is true.')
param projectName string = ''

@description('Tags applied to Foundry resources.')
param tags object = {}

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

output foundryName string = foundryAccount.name
output foundryEndpoint string = foundryAccount.properties.endpoint
output foundryProjectName string = deployProject ? foundryProject!.name : ''
output foundryProjectPrincipalId string = deployProject ? foundryProject!.identity!.principalId : ''
