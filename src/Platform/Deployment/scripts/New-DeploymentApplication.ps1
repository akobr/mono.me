#Requires -Version 7.4
<#
.SYNOPSIS
    Cookbook, step 11.2: the identity of the deployment workflows, "2S Platform Deployment".

.DESCRIPTION
    Finds or creates the single-tenant application, its service principal and a federated credential for the GitHub
    environment (issuer token.actions.githubusercontent.com, subject repo:<repository>:environment:<environment>). No
    secret: the workflows sign in with azure/login and OIDC.

    Its Azure roles are assigned by main.bicep (modules/access.bicep) once deploymentPrincipalId of
    parameters/prod.bicepparam holds the printed object id of the service principal: Website Contributor on the Function
    App, Key Vault Secrets User on the secret cosmos-connection, Storage Blob Data Contributor on the container
    deployments. Safe to run again.

.EXAMPLE
    $deployment = ./scripts/New-DeploymentApplication.ps1
    $deployment.PrincipalId   # deploymentPrincipalId of parameters/prod.bicepparam
#>
[CmdletBinding()]
param(
    [string] $DisplayName = '2S Platform Deployment',
    [string] $Repository = 'akobr/mono.me',
    # <environment>.42.<area dotted path> (decisions, "GitHub environments")
    [string] $GitHubEnvironment = 'prod.42.Platform'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

Import-Module (Join-Path $PSScriptRoot 'EntraApplications.psm1') -Force

$account = az account show --query '{tenantId:tenantId, subscriptionId:id}' -o json | ConvertFrom-Json

$appId = Get-OrCreateApplication -DisplayName $DisplayName -SignInAudience AzureADMyOrg
$principalId = Get-OrCreateServicePrincipal -AppId $appId

Set-FederatedCredential -AppId $appId `
    -Name ("github-" + ($GitHubEnvironment -replace '[^A-Za-z0-9_-]', '-')) `
    -Issuer 'https://token.actions.githubusercontent.com' `
    -Subject "repo:${Repository}:environment:$GitHubEnvironment" `
    -Description "GitHub Actions, environment $GitHubEnvironment"

Write-Host "Put deploymentPrincipalId = '$principalId' into parameters/prod.bicepparam, commit it and run main.bicep again."
Write-Host "AZURE_CLIENT_ID of the GitHub environment: $appId (Set-GitHubEnvironment.ps1 -DeploymentClientId)."

[pscustomobject]@{
    ClientId       = $appId
    PrincipalId    = $principalId
    TenantId       = $account.tenantId
    SubscriptionId = $account.subscriptionId
}
