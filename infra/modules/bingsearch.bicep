@description('Azure region for the Bing Custom Search resource.')
param location string = 'global'

@description('Bing Custom Search account name.')
param accountName string

@description('Tags applied to the Bing Custom Search resource.')
param tags object = {}

resource bingCustomSearchAccount 'Microsoft.Bing/accounts@2020-06-10' = {
  name: accountName
  location: location
  kind: 'Bing.GroundingCustomSearch'
  sku: {
    name: 'G2'
  }
  tags: tags
}

output bingCustomSearchAccountName string = bingCustomSearchAccount.name
output bingCustomSearchResourceId string = bingCustomSearchAccount.id
