# Shared steps of New-ByokApplication.ps1 and New-DeploymentApplication.ps1: find or create an Entra application, its
# service principal and a federated identity credential, with the Azure CLI. Bicep does not manage Entra objects.

Set-StrictMode -Version Latest

# A module does not see the preference variables of the script that imports it.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Get-OrCreateApplication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $DisplayName,
        [Parameter(Mandatory)] [ValidateSet('AzureADMyOrg', 'AzureADMultipleOrgs')] [string] $SignInAudience
    )

    $escaped = $DisplayName.Replace("'", "''")
    $apps = @(az ad app list --filter "displayName eq '$escaped'" --query '[].{appId:appId, id:id, audience:signInAudience, passwords:length(passwordCredentials), certificates:length(keyCredentials)}' -o json | ConvertFrom-Json)
    if ($apps.Count -gt 1) {
        throw "More than one application is named '$DisplayName' ($($apps.appId -join ', ')). Remove the extra ones first."
    }

    if ($apps.Count -eq 1) {
        $app = $apps[0]
        Write-Host "Application '$DisplayName' exists: $($app.appId)"
        if ($app.audience -ne $SignInAudience) {
            throw "Application '$DisplayName' has the sign-in audience $($app.audience), not $SignInAudience."
        }

        if ($app.passwords -gt 0 -or $app.certificates -gt 0) {
            Write-Warning "Application '$DisplayName' has $($app.passwords) secret(s) and $($app.certificates) certificate(s): it is meant to have none, its credential is the federation."
        }

        return $app.appId
    }

    $appId = az ad app create --display-name $DisplayName --sign-in-audience $SignInAudience --query appId -o tsv
    Write-Host "Application '$DisplayName' created: $appId"
    return $appId
}

function Get-OrCreateServicePrincipal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $AppId
    )

    $id = az ad sp list --filter "appId eq '$AppId'" --query '[0].id' -o tsv
    if ($id) {
        return $id
    }

    $id = az ad sp create --id $AppId --query id -o tsv
    Write-Host "Service principal of $AppId created: $id"
    return $id
}

function Set-FederatedCredential {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $AppId,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Issuer,
        [Parameter(Mandatory)] [string] $Subject,
        [Parameter(Mandatory)] [string] $Description
    )

    $credential = [ordered]@{
        name        = $Name
        issuer      = $Issuer
        subject     = $Subject
        audiences   = @('api://AzureADTokenExchange')
        description = $Description
    }

    $existing = @(az ad app federated-credential list --id $AppId -o json | ConvertFrom-Json | Where-Object { $_.name -eq $Name })
    $audiencesMatch = $existing.Count -eq 1 -and (@($existing[0].audiences) -join ' ') -eq ($credential.audiences -join ' ')
    if ($audiencesMatch -and $existing[0].issuer -eq $Issuer -and $existing[0].subject -eq $Subject) {
        Write-Host "Federated credential '$Name' is up to date (subject $Subject)."
        return
    }

    $file = New-TemporaryFile
    try {
        $credential | ConvertTo-Json | Set-Content -Path $file.FullName -Encoding utf8NoBOM
        if ($existing.Count -eq 1) {
            Write-Host "Federated credential '$Name' differs (subject $($existing[0].subject), audiences $(@($existing[0].audiences) -join ', ')); updating it."
            az ad app federated-credential update --id $AppId --federated-credential-id $existing[0].id --parameters $file.FullName --output none
        }
        else {
            az ad app federated-credential create --id $AppId --parameters $file.FullName --output none
            Write-Host "Federated credential '$Name' created (subject $Subject)."
        }
    }
    finally {
        Remove-Item -Path $file.FullName -Force
    }
}

Export-ModuleMember -Function Get-OrCreateApplication, Get-OrCreateServicePrincipal, Set-FederatedCredential
