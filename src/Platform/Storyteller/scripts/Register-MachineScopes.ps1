#Requires -Version 7.0

<#
.SYNOPSIS
Creates the Entra ID app roles and the WorkOS permissions of the Storyteller machine scopes.

.DESCRIPTION
Storyteller maps every machine scope 1:1 to one app role of the API app registration (Entra ID) and to one
WorkOS permission (AuthKit). This script creates what is missing and prints the settings that map them:
Auth:AppRoles:<Scope> for Entra ID and Auth:AuthKit:PermissionMap:<slug> for AuthKit.

The script is idempotent: an app role whose value is already the scope string (also with an App. prefix) and a
permission whose slug already exists are kept as they are. It never changes or deletes anything else.
Use -WhatIf to see what it would create.

.PARAMETER EntraApplicationId
The application (client) ID of the Storyteller API app registration, the value of Auth:ClientId. Needs the
Azure CLI (az), signed in with rights to update that registration.

.PARAMETER WorkOsApiKey
A WorkOS API key (sk_...) of the environment AuthKit uses. Defaults to the WORKOS_API_KEY environment variable.

.PARAMETER WorkOsSlugPrefix
The prefix of the WorkOS permission slugs. The default gives storyteller:annotation-read and so on.

.PARAMETER WorkOsApiBaseUrl
The WorkOS API. Change it only for a proxy or a test double.

.EXAMPLE
./Register-MachineScopes.ps1 -EntraApplicationId 303a7632-0000-0000-0000-000000000000

.EXAMPLE
$env:WORKOS_API_KEY = 'sk_test_...'; ./Register-MachineScopes.ps1 -WorkOs -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $EntraApplicationId,

    [switch] $WorkOs,

    [string] $WorkOsApiKey = $env:WORKOS_API_KEY,

    [string] $WorkOsSlugPrefix = 'storyteller:',

    [string] $WorkOsApiBaseUrl = 'https://api.workos.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The catalogue of MachineScope (Abstractions.Access/src/MachineScope.cs): the enum name is the settings key, the
# scope string is the app-role value and the PermissionMap value.
$MachineScopes = @(
    @{ Name = 'AnnotationRead'; Scope = 'Annotation.Read'; Slug = 'annotation-read'; Description = 'Read annotations.' }
    @{ Name = 'AnnotationWrite'; Scope = 'Annotation.Write'; Slug = 'annotation-write'; Description = 'Write and delete single annotations.' }
    @{ Name = 'ConfigurationRead'; Scope = 'Configuration.Read'; Slug = 'configuration-read'; Description = 'Read configurations, schemas, templates and key/values.' }
    @{ Name = 'ConfigurationWrite'; Scope = 'Configuration.Write'; Slug = 'configuration-write'; Description = 'Write configurations and key/values.' }
    @{ Name = 'SecretsRead'; Scope = 'Secrets.Read'; Slug = 'secrets-read'; Description = 'Receive the values of key/value secrets.' }
    @{ Name = 'SecretsWrite'; Scope = 'Secrets.Write'; Slug = 'secrets-write'; Description = 'Write key/value secrets.' }
    @{ Name = 'ConfigurationTransfer'; Scope = 'Configuration.Transfer'; Slug = 'configuration-transfer'; Description = 'Export and import views and key/values (management).' }
    @{ Name = 'SchemaWrite'; Scope = 'Schema.Write'; Slug = 'schema-write'; Description = 'Write and delete schemas (management).' }
    @{ Name = 'TemplateWrite'; Scope = 'Template.Write'; Slug = 'template-write'; Description = 'Write and delete templates (management).' }
    @{ Name = 'AnnotationManage'; Scope = 'Annotation.Manage'; Slug = 'annotation-manage'; Description = 'Bulk create, delete and restore annotations (management).' }
    @{ Name = 'KeyValueManage'; Scope = 'KeyValue.Manage'; Slug = 'keyvalue-manage'; Description = 'Protect and bulk restore key/values (management).' }
)

function Register-EntraAppRoles {
    param([string] $ApplicationId)

    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw 'The Azure CLI (az) is needed for -EntraApplicationId. Install it and run az login.'
    }

    $application = az ad app show --id $ApplicationId --query '{id:id, appRoles:appRoles}' --output json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $application) {
        throw "The app registration $ApplicationId cannot be read."
    }

    # Graph replaces the whole collection on PATCH, so the existing roles go back unchanged, writable members only.
    $roles = @($application.appRoles | ForEach-Object {
            [ordered]@{
                allowedMemberTypes = @($_.allowedMemberTypes)
                description        = $_.description
                displayName        = $_.displayName
                id                 = $_.id
                isEnabled          = $_.isEnabled
                value              = $_.value
            }
        })
    $settings = [ordered]@{}
    $added = @()

    foreach ($machineScope in $MachineScopes) {
        $existing = $roles | Where-Object { $_.value -ceq $machineScope.Scope -or $_.value -ceq "App.$($machineScope.Scope)" } | Select-Object -First 1

        if ($existing) {
            $settings[$machineScope.Name] = $existing.id
            continue
        }

        $role = [ordered]@{
            allowedMemberTypes = @('Application')
            description        = "Storyteller machine scope $($machineScope.Scope): $($machineScope.Description)"
            displayName        = "Storyteller $($machineScope.Scope)"
            id                 = [guid]::NewGuid().ToString()
            isEnabled          = $true
            value              = $machineScope.Scope
        }
        $roles += $role
        $added += $machineScope.Scope
        $settings[$machineScope.Name] = $role.id
    }

    if ($added.Count -eq 0) {
        Write-Host "Entra ID: every machine scope already has an app role on $ApplicationId."
    }
    elseif ($PSCmdlet.ShouldProcess($ApplicationId, "Add the app roles $($added -join ', ')")) {
        $body = New-TemporaryFile
        try {
            @{ appRoles = $roles } | ConvertTo-Json -Depth 5 | Set-Content -Path $body -Encoding utf8NoBOM
            az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$($application.id)" --headers 'Content-Type=application/json' --body "@$body" --output none
            if ($LASTEXITCODE -ne 0) {
                throw "The app roles of $ApplicationId could not be updated."
            }
        }
        finally {
            Remove-Item -Path $body -ErrorAction SilentlyContinue
        }

        Write-Host "Entra ID: added the app roles $($added -join ', ')."
    }
    else {
        Write-Host "Entra ID: would add the app roles $($added -join ', '). The IDs of the new roles below are examples."
    }

    Write-Host ''
    Write-Host 'Settings (environment variables):'
    foreach ($name in $settings.Keys) {
        Write-Host "Auth__AppRoles__$name=$($settings[$name])"
    }
    Write-Host ''
}

function Invoke-WorkOs {
    param([string] $Method, [string] $Path, [object] $Body)

    $request = @{
        Method  = $Method
        Uri     = "$($WorkOsApiBaseUrl.TrimEnd('/'))$Path"
        Headers = @{ Authorization = "Bearer $WorkOsApiKey" }
    }

    if ($Body) {
        $request.ContentType = 'application/json'
        $request.Body = $Body | ConvertTo-Json -Depth 5
    }

    Invoke-RestMethod @request
}

function Register-WorkOsPermissions {
    if (-not $WorkOsApiKey) {
        throw 'A WorkOS API key is needed: pass -WorkOsApiKey or set WORKOS_API_KEY.'
    }

    $existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $after = $null

    do {
        $path = '/authorization/permissions?limit=100' + ($after ? "&after=$([uri]::EscapeDataString($after))" : '')
        $page = Invoke-WorkOs -Method Get -Path $path
        foreach ($permission in $page.data) {
            [void] $existing.Add($permission.slug)
        }

        $after = $page.list_metadata.after
    } while ($after)

    $added = @()

    foreach ($machineScope in $MachineScopes) {
        $slug = "$WorkOsSlugPrefix$($machineScope.Slug)"

        if ($existing.Contains($slug)) {
            continue
        }

        if ($PSCmdlet.ShouldProcess($slug, 'Create the WorkOS permission')) {
            Invoke-WorkOs -Method Post -Path '/authorization/permissions' -Body @{
                slug        = $slug
                name        = "Storyteller $($machineScope.Scope)"
                description = "Storyteller machine scope $($machineScope.Scope): $($machineScope.Description)"
            } | Out-Null
        }

        $added += $slug
    }

    if ($added.Count -eq 0) {
        Write-Host 'WorkOS: every machine scope already has a permission.'
    }
    else {
        $verb = $WhatIfPreference ? 'would create' : 'created'
        Write-Host "WorkOS: $verb the permissions $($added -join ', ')."
    }

    Write-Host ''
    Write-Host 'Settings (JSON, for example local.settings.json):'
    foreach ($machineScope in $MachineScopes) {
        Write-Host "`"Auth:AuthKit:PermissionMap:$WorkOsSlugPrefix$($machineScope.Slug)`": `"$($machineScope.Scope)`","
    }
    Write-Host ''
    Write-Host 'Settings (environment variables; a : in the slug becomes __):'
    foreach ($machineScope in $MachineScopes) {
        $slug = "$WorkOsSlugPrefix$($machineScope.Slug)" -replace ':', '__'
        Write-Host "Auth__AuthKit__PermissionMap__$slug=$($machineScope.Scope)"
    }
    Write-Host ''
}

if (-not $EntraApplicationId -and -not $WorkOs) {
    throw 'Pass -EntraApplicationId for Entra ID, -WorkOs for WorkOS, or both.'
}

if ($EntraApplicationId) {
    Register-EntraAppRoles -ApplicationId $EntraApplicationId
}

if ($WorkOs) {
    Register-WorkOsPermissions
}
