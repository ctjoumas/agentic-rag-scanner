@description('Azure region for App Configuration store.')
param location string

@description('App Configuration store name.')
param storeName string

@description('Tags applied to App Configuration resources.')
param tags object = {}

resource appConfigStore 'Microsoft.AppConfiguration/configurationStores@2024-05-01' = {
  name: storeName
  location: location
  sku: {
    name: 'standard'
  }
  tags: tags
  properties: {
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
    enablePurgeProtection: false
  }
}

output appConfigStoreName string = appConfigStore.name
output appConfigEndpoint string = appConfigStore.properties.endpoint
