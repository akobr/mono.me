#Requires -Version 7.4
<#
.SYNOPSIS
    Cookbook, step 11.1: the multi-tenant BYOK application "2S Platform Customer Keys".

.DESCRIPTION
    Finds or creates the application (sign-in audience AzureADMultipleOrgs, no secret), its service principal, and its
    only credential: a federation with the API's user-assigned managed identity (issuer the tenant, subject the
    identity's principal id, audience api://AzureADTokenExchange). Customers consent to the application and grant its
    service principal Key Vault Crypto Service Encryption User on their key.

    Run it after the first run of main.bicep (the identity must exist), as an Application Administrator of the tenant.
    Then run main.bicep again with PLATFORM_BYOK_CLIENT_ID set to the printed application id. Safe to run again.

    Before the first BYOK customer, start publisher verification of the application (a Microsoft Partner Network id),
    or customers see an "unverified" warning when they consent.

.EXAMPLE
    $env:PLATFORM_BYOK_CLIENT_ID = ./scripts/New-ByokApplication.ps1
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'prod-2s-eusec-rg',
    [string] $IdentityName = 'prod-2s-api-mi',
    [string] $DisplayName = '2S Platform Customer Keys'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

Import-Module (Join-Path $PSScriptRoot 'EntraApplications.psm1') -Force

$tenantId = az account show --query tenantId -o tsv
$principalId = az identity show --resource-group $ResourceGroup --name $IdentityName --query principalId -o tsv

$appId = Get-OrCreateApplication -DisplayName $DisplayName -SignInAudience AzureADMultipleOrgs
$null = Get-OrCreateServicePrincipal -AppId $appId

Set-FederatedCredential -AppId $appId `
    -Name $IdentityName `
    -Issuer "https://login.microsoftonline.com/$tenantId/v2.0" `
    -Subject $principalId `
    -Description "The managed identity $IdentityName of the 2S API"

Write-Host "Set PLATFORM_BYOK_CLIENT_ID=$appId and run main.bicep again (KeyValues__Protection__Byok__ClientId)."
Write-Output $appId
