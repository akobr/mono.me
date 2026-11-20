#Requires -Version 7.4
<#
.SYNOPSIS
    The MCP server, step 2: the audience of the MCP server's actor token, "2S Platform Token Exchange".

.DESCRIPTION
    Finds or creates the single-tenant application (no secret, no certificate, no redirect URI), its service principal,
    and its App ID URI api://<application id>. It stands for the Storyteller user token exchange and nothing else: the
    MCP server's managed identity (prod-2s-mcp-mi) requests a token for api://<application id>/.default and sends it to
    the API as its actor_token, and the API's actor mcp accepts only that audience, from that identity
    (docs/Platform/Storyteller/mcp.md, "Credentials of the actor").

    The tokens are v1 (api.requestedAccessTokenVersion unset): issuer https://sts.windows.net/<tenant>/ and the App ID URI
    as aud, the pair main.bicep gives the API. The script stops if the application asks for v2 tokens, or if it has
    another App ID URI, rather than change either.

    Run it before the first run of main.bicep with mcpEnabled, as an Application Administrator of the tenant. Then put
    the printed application id into mcpExchangeAppId of parameters/prod.bicepparam and commit it. Safe to run again.

    With -McpIdentityName, once the identity exists (after that run of main.bicep), the application also accepts only
    that identity: the identity gets the application's default app role, and the service principal requires an
    assignment (appRoleAssignmentRequired), so Entra issues no token for the audience to any other identity of the
    tenant. The API pins the identity's object id anyway; this is defense in depth. The assignment comes first, so the
    server is never locked out.

.EXAMPLE
    $exchange = ./scripts/New-TokenExchangeApplication.ps1
    $exchange.ClientId        # mcpExchangeAppId of parameters/prod.bicepparam
    $exchange.IdentifierUri   # api://<the same id>

.EXAMPLE
    ./scripts/New-TokenExchangeApplication.ps1 -McpIdentityName prod-2s-mcp-mi
#>
[CmdletBinding()]
param(
    [string] $DisplayName = '2S Platform Token Exchange',

    [Parameter(HelpMessage = "The MCP server's identity, once it exists: only it may then get tokens for the application.")]
    [string] $McpIdentityName = '',

    [string] $ResourceGroup = 'prod-2s-eusec-rg'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

Import-Module (Join-Path $PSScriptRoot 'EntraApplications.psm1') -Force

$appId = Get-OrCreateApplication -DisplayName $DisplayName -SignInAudience AzureADMyOrg
$principalId = Get-OrCreateServicePrincipal -AppId $appId

# The App ID URI that every managed identity token of the actor carries as aud (v1 tokens): api://<application id>,
# which Entra allows without a verified domain.
$identifierUri = "api://$appId"
$app = az ad app show --id $appId --query '{uris:identifierUris, version:api.requestedAccessTokenVersion}' -o json | ConvertFrom-Json
$uris = @($app.uris | Where-Object { $_ })

if ($uris -cnotcontains $identifierUri) {
    if ($uris.Count -gt 0) {
        throw "Application '$DisplayName' has the App ID URI(s) $($uris -join ', '), not $identifierUri. The API's actor pins the audience: fix it by hand, or remove the other URI(s) first."
    }

    az ad app update --id $appId --identifier-uris $identifierUri --output none
    Write-Host "App ID URI of '$DisplayName' set: $identifierUri"
}
else {
    Write-Host "App ID URI of '$DisplayName' is $identifierUri."
}

if ($null -ne $app.version -and [int] $app.version -ne 1) {
    throw "Application '$DisplayName' asks for v$($app.version) access tokens (api.requestedAccessTokenVersion). main.bicep gives the API the v1 pair: the issuer https://sts.windows.net/<tenant>/ with the App ID URI as aud. Set it back to null (the manifest) and run the script again."
}

if ($McpIdentityName) {
    $identityPrincipalId = az identity show --resource-group $ResourceGroup --name $McpIdentityName --query principalId -o tsv
    $graphBase = 'https://graph.microsoft.com/v1.0'

    # The default app role (an empty GUID): an assignment without a role, which needs no role on the application.
    $assigned = @(az rest --method GET --url "$graphBase/servicePrincipals/$principalId/appRoleAssignedTo" --query "value[?principalId=='$identityPrincipalId'].id" -o json | ConvertFrom-Json)
    if ($assigned.Count -eq 0) {
        $file = New-TemporaryFile
        try {
            [ordered]@{
                principalId = $identityPrincipalId
                resourceId  = $principalId
                appRoleId   = '00000000-0000-0000-0000-000000000000'
            } | ConvertTo-Json | Set-Content -Path $file.FullName -Encoding utf8NoBOM
            az rest --method POST --url "$graphBase/servicePrincipals/$principalId/appRoleAssignedTo" --headers 'Content-Type=application/json' --body "@$($file.FullName)" --output none
        }
        finally {
            Remove-Item -Path $file.FullName -Force
        }

        Write-Host "The identity $McpIdentityName ($identityPrincipalId) is assigned to '$DisplayName'."
    }
    else {
        Write-Host "The identity $McpIdentityName ($identityPrincipalId) is already assigned to '$DisplayName'."
    }

    $required = az ad sp show --id $appId --query appRoleAssignmentRequired -o tsv
    if ($required -ne 'true') {
        az ad sp update --id $appId --set appRoleAssignmentRequired=true --output none
        Write-Host "'$DisplayName' now requires an assignment: no other identity of the tenant gets a token for it."
    }
    else {
        Write-Host "'$DisplayName' requires an assignment."
    }
}
else {
    Write-Host "Put mcpExchangeAppId = '$appId' into parameters/prod.bicepparam and commit it."

    if ((az ad sp show --id $appId --query appRoleAssignmentRequired -o tsv) -eq 'true') {
        Write-Warning "'$DisplayName' requires an assignment: only assigned identities get tokens for it. Run the script with -McpIdentityName for a new or re-created MCP server identity."
    }
}

[pscustomobject]@{
    ClientId      = $appId
    IdentifierUri = $identifierUri
    PrincipalId   = $principalId
}
