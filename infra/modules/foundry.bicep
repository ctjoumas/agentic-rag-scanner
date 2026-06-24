@description('Azure region for Microsoft Foundry account.')
param location string

@description('Microsoft Foundry account name.')
param accountName string

@description('Tags applied to Foundry resources.')
param tags object = {}

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: accountName
  location: location
  sku: {
    name: 'S0'
  }
  kind: 'AIServices'
  tags: tags
  properties: {
    customSubDomainName: accountName
    publicNetworkAccess: 'Enabled'
  }
}

output foundryName string = foundryAccount.name
output foundryEndpoint string = foundryAccount.properties.endpoint
