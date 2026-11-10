#Requires -Version 7.4
<#
.SYNOPSIS
    Cookbook, step 15: the GitHub environment of the deployment workflows.

.DESCRIPTION
    Creates or updates the environment (a required reviewer, deployments from the branch main only) and sets its
    variables from the outputs of the deployment of main.bicep and the parameters. The one secret,
    AZURE_STATIC_WEB_APPS_API_TOKEN, goes from the Static Web App to GitHub without being printed.

    Needs the Azure CLI signed in to the environment's subscription and the GitHub CLI signed in as an admin of the
    repository. Variables whose value is empty are left as they are. Safe to run again.

.EXAMPLE
    ./scripts/Set-GitHubEnvironment.ps1 -DeploymentClientId <appId> -BaseAccountId user_01... `
        -SentryDsn https://...@o0.ingest.de.sentry.io/0 -PostHogProjectToken phc_...
#>
[CmdletBinding()]
param(
    [string] $Repository = 'akobr/mono.me',
    [string] $Environment = 'production',

    [string] $Subscription = 'prod-42fornet-sub',

    [Parameter(HelpMessage = 'The name of the subscription deployment of main.bicep, whose outputs are read.')]
    [string] $DeploymentName = 'prod-2s-main',

    [Parameter(Mandatory)]
    [string] $DeploymentClientId,

    [Parameter(Mandatory)]
    [string] $BaseAccountId,

    [string] $SentryDsn = '',
    [string] $PostHogProjectToken = '',
    [string] $PostHogHost = 'https://eu.i.posthog.com',

    [Parameter(HelpMessage = 'The GitHub login of the required reviewer; the signed-in user by default.')]
    [string] $Reviewer = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# PowerShell 7.4+: a native command (az, gh) that exits with a nonzero code throws, so the script stops there.
$PSNativeCommandUseErrorActionPreference = $true

function Set-GitHubVariable([string] $Name, [string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        Write-Host "  $Name (empty, left as it is)"
        return
    }

    gh variable set $Name --repo $Repository --env $Environment --body $Value
    Write-Host "  $Name"
}

# Writes the value to the standard input of gh, without a trailing new line and without it ever being an argument.
function Set-GitHubSecret([string] $Name, [string] $Value) {
    $gh = (Get-Command gh -CommandType Application | Select-Object -First 1).Source
    $info = [System.Diagnostics.ProcessStartInfo]::new($gh)
    foreach ($argument in @('secret', 'set', $Name, '--repo', $Repository, '--env', $Environment)) {
        $info.ArgumentList.Add($argument)
    }

    $info.RedirectStandardInput = $true
    $info.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($info)
    $process.StandardInput.Write($Value)
    $process.StandardInput.Close()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "gh secret set $Name failed with exit code $($process.ExitCode)."
    }

    Write-Host "  $Name (secret)"
}

az account set --subscription $Subscription
$outputs = az deployment sub show --name $DeploymentName --query properties.outputs -o json | ConvertFrom-Json
function Get-Output([string] $Name) {
    return $outputs.$Name.value
}

if (-not $Reviewer) {
    $Reviewer = gh api user --jq .login
}

$reviewerId = [int64](gh api "users/$Reviewer" --jq .id)

Write-Host "Environment '$Environment' of $Repository, required reviewer $Reviewer"
$environmentBody = @{
    reviewers                = @(@{ type = 'User'; id = $reviewerId })
    deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true }
} | ConvertTo-Json -Depth 5
$environmentBody | gh api --method PUT "repos/$Repository/environments/$Environment" --input - --silent

# Deployments from the branch main only: remove every other policy (a tag policy named main too), and add the branch
# policy main when it is missing.
$policiesPath = "repos/$Repository/environments/$Environment/deployment-branch-policies"
$policies = @(gh api $policiesPath --paginate --jq '.branch_policies[] | {id, name, type}' | ForEach-Object { $_ | ConvertFrom-Json })
foreach ($policy in $policies | Where-Object { -not ($_.name -ceq 'main' -and $_.type -ceq 'branch') }) {
    gh api --method DELETE "$policiesPath/$($policy.id)" --silent
    Write-Host "  removed the $($policy.type) policy '$($policy.name)'"
}

if (-not ($policies | Where-Object { $_.name -ceq 'main' -and $_.type -ceq 'branch' })) {
    gh api --method POST $policiesPath -f name=main -f type=branch --silent
}

Write-Host 'Variables:'
Set-GitHubVariable 'AZURE_CLIENT_ID' $DeploymentClientId
Set-GitHubVariable 'AZURE_TENANT_ID' (Get-Output 'tenantId')
Set-GitHubVariable 'AZURE_SUBSCRIPTION_ID' (Get-Output 'subscriptionId')
Set-GitHubVariable 'AZURE_RESOURCE_GROUP' (Get-Output 'resourceGroupName')
Set-GitHubVariable 'AZURE_FUNCTIONAPP_NAME' (Get-Output 'functionAppName')
Set-GitHubVariable 'AZURE_KEYVAULT_NAME' (Get-Output 'keyVaultName')
Set-GitHubVariable 'STORYTELLER_BASE_ACCOUNT_ID' $BaseAccountId
Set-GitHubVariable 'VITE_API_BASE_URL' (Get-Output 'apiBaseUrl')
Set-GitHubVariable 'VITE_AUTH_API_HOSTNAME' (Get-Output 'authKitDomain')
Set-GitHubVariable 'VITE_ENVIRONMENT_NAME' $Environment
Set-GitHubVariable 'VITE_SENTRY_DSN' $SentryDsn
Set-GitHubVariable 'VITE_POSTHOG_PROJECT_TOKEN' $PostHogProjectToken
Set-GitHubVariable 'VITE_POSTHOG_HOST' $PostHogHost

Write-Host 'Secrets:'
$token = az staticwebapp secrets list --resource-group (Get-Output 'resourceGroupName') --name (Get-Output 'staticWebAppName') --query properties.apiKey -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "The Static Web App $(Get-Output 'staticWebAppName') returned no deployment token."
}

try {
    Set-GitHubSecret 'AZURE_STATIC_WEB_APPS_API_TOKEN' $token
}
finally {
    Remove-Variable -Name token
}
