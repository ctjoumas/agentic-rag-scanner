@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Base name used to derive resource names.')
param baseName string = 'agenticragscanner'

@description('Tags applied to all resources.')
param tags object = {}

@description('Name of the SQL database in Cosmos DB.')
param cosmosDatabaseName string = 'agentic-rag-scanner'

@description('Name of the SQL container in Cosmos DB.')
param cosmosContainerName string = 'results'

@description('Create a Foundry project under the Foundry account.')
param deployFoundryProject bool = false

@description('Foundry project name when deployFoundryProject is true.')
param foundryProjectName string = ''

var suffix = toLower(substring(uniqueString(subscription().id, resourceGroup().id), 0, 6))
var compactBaseName = toLower(replace(baseName, '-', ''))

var storageAccountName = take('${compactBaseName}${suffix}st', 24)
var cosmosAccountName = take('${compactBaseName}-${suffix}-cosmos', 44)
var foundryAccountName = take('${compactBaseName}-${suffix}-foundry', 64)
var keyVaultName = take('${compactBaseName}-${suffix}-kv', 24)
var appConfigStoreName = take('${compactBaseName}-${suffix}-appcs', 50)
var appInsightsName = '${baseName}-${suffix}-appi'
var logAnalyticsWorkspaceName = '${baseName}-${suffix}-law'

module cosmosModule './modules/cosmos.bicep' = {
  name: 'cosmosDeployment'
  params: {
    location: location
    accountName: cosmosAccountName
    databaseName: cosmosDatabaseName
    containerName: cosmosContainerName
    tags: tags
  }
}

module storageModule './modules/storage.bicep' = {
  name: 'storageDeployment'
  params: {
    location: location
    accountName: storageAccountName
    tags: tags
  }
}

module foundryModule './modules/foundry.bicep' = {
  name: 'foundryDeployment'
  params: {
    location: location
    accountName: foundryAccountName
    deployProject: deployFoundryProject
    projectName: foundryProjectName
    tags: tags
  }
}

module appConfigModule './modules/appconfig.bicep' = {
  name: 'appConfigDeployment'
  params: {
    location: location
    storeName: appConfigStoreName
    tags: tags
  }
}

module keyVaultModule './modules/keyvault.bicep' = {
  name: 'keyVaultDeployment'
  params: {
    location: location
    vaultName: keyVaultName
    tenantId: subscription().tenantId
    tags: tags
  }
}

module appInsightsModule './modules/appinsights.bicep' = {
  name: 'appInsightsDeployment'
  params: {
    location: location
    appInsightsName: appInsightsName
    workspaceName: logAnalyticsWorkspaceName
    tags: tags
  }
}

output cosmosAccountName string = cosmosModule.outputs.cosmosAccountName
output storageAccountName string = storageModule.outputs.storageAccountName
output foundryName string = foundryModule.outputs.foundryName
output keyVaultName string = keyVaultModule.outputs.keyVaultName
output appInsightsName string = appInsightsModule.outputs.appInsightsName
output appConfigStoreName string = appConfigModule.outputs.appConfigStoreName
output foundryProjectName string = foundryModule.outputs.foundryProjectName
output foundryProjectPrincipalId string = foundryModule.outputs.foundryProjectPrincipalId

output cosmosEndpoint string = cosmosModule.outputs.cosmosEndpoint
output storageBlobEndpoint string = storageModule.outputs.storageBlobEndpoint
output foundryEndpoint string = foundryModule.outputs.foundryEndpoint
output keyVaultUri string = keyVaultModule.outputs.keyVaultUri
output appConfigEndpoint string = appConfigModule.outputs.appConfigEndpoint
output appInsightsConnectionString string = appInsightsModule.outputs.appInsightsConnectionString
