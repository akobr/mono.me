#Requires -Version 7.3
<#
.SYNOPSIS
    Cookbook, step 1.3-1.4: prepares the subscription of an environment for main.bicep.

.DESCRIPTION
    Selects the subscription, registers the resource providers of the 2S Platform and waits for them, checks that the
    Flex Consumption plan is offered in the region, and stops if the subscription already has a free-tier Cosmos DB
    account (there is one per subscription, and main.bicep must create it).

    The upgrade of a free account to pay-as-you-go and the rename of the subscription (steps 1.1-1.2) are done in the
    portal before. Safe to run again.

.EXAMPLE
    ./scripts/Initialize-Subscription.ps1 -Subscription prod-42fornet-sub
#>
[CmdletBinding()]
param(
    [string] $Subscription = 'prod-42fornet-sub',
    [string] $Location = 'swedencentral',
    [string] $CosmosAccountName = 'prod-2s-42fornet-cdb',
    [int] $TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$providers = @(
    'Microsoft.Web'
    'Microsoft.App'
    'Microsoft.DocumentDB'
    'Microsoft.KeyVault'
    'Microsoft.Storage'
    'Microsoft.Insights'
    'Microsoft.OperationalInsights'
    'Microsoft.ManagedIdentity'
    'Microsoft.Consumption'
)

az account set --subscription $Subscription
$account = az account show --query '{name:name, id:id, tenant:tenantId}' -o json | ConvertFrom-Json
Write-Host "Subscription $($account.name) ($($account.id)), tenant $($account.tenant)"

foreach ($provider in $providers) {
    az provider register --namespace $provider --output none
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ($true) {
    $pending = @($providers | Where-Object { (az provider show --namespace $_ --query registrationState -o tsv) -ne 'Registered' })
    if ($pending.Count -eq 0) {
        break
    }

    if ((Get-Date) -gt $deadline) {
        throw "Not registered after $TimeoutSeconds s: $($pending -join ', ')."
    }

    Write-Host "Waiting for: $($pending -join ', ')"
    Start-Sleep -Seconds 15
}

Write-Host "Providers registered: $($providers -join ', ')"

$flex = @(az functionapp list-flexconsumption-locations --query "[?name=='$Location'].name" -o tsv)
if ($flex -notcontains $Location) {
    throw "Flex Consumption is not offered in $Location. Revisit the region decision before anything is created."
}

Write-Host "Flex Consumption is offered in $Location."

# The free tier is chosen when an account is created, and a subscription has one free-tier account.
$freeTier = @(az cosmosdb list --query "[?enableFreeTier].name" -o tsv | Where-Object { $_ })
$others = @($freeTier | Where-Object { $_ -ne $CosmosAccountName })
if ($others.Count -gt 0) {
    throw "The subscription's free tier of Cosmos DB is taken by $($others -join ', '): main.bicep would fail to create $CosmosAccountName with it."
}

if ($freeTier -contains $CosmosAccountName) {
    Write-Host "The free tier belongs to $CosmosAccountName already."
}
else {
    Write-Host 'No Cosmos DB account has the free tier yet: main.bicep takes it.'
}
