#Requires -Version 7
<#
.SYNOPSIS
  Installs the MCP Dataverse server locally and wires it into OpenCode and Pi.
.DESCRIPTION
  1. Builds and installs the .NET global tool `mcp-dataverse`.
  2. Registers the MCP server in the global OpenCode config (~/.config/opencode/opencode.json)
     and the global shared MCP config (~/.config/mcp/mcp.json, read by pi-mcp-adapter).
  3. Installs pi-mcp-adapter (Pi has no built-in MCP) and enables client-side approval
     for ConfirmWrite.
  4. Copies the skills to ~/.agents/skills/ (read by both Pi and OpenCode).
.PARAMETER EnvironmentUrl
  Dataverse environment URL, e.g. https://yourorg.crm.dynamics.com
.PARAMETER ClientId / TenantId / ClientSecret
  Optional. If set, they are written into the MCP configs (S2S mode). Otherwise the
  server uses delegated interactive auth (browser login, persistent token cache).
.EXAMPLE
  ./install.ps1 -EnvironmentUrl https://yourorg.crm.dynamics.com
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$EnvironmentUrl,
    [string]$AppId,
    [string]$ClientId,
    [string]$TenantId,
    [string]$ClientSecret,
    [string]$RepoRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$serverName = 'mcp-dataverse'
$packageName = 'Mcp.Dataverse.Stdio'

# --- 1. build & install the dotnet tool -----------------------------------
Write-Host '==> Building tool package...'
dotnet pack "$RepoRoot/src/Mcp.Dataverse.Stdio/Mcp.Dataverse.Stdio.csproj" -c Release -o "$RepoRoot/nupkgs"
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed' }

Write-Host '==> Installing global tool mcp-dataverse...'
dotnet tool update --global $packageName --add-source "$RepoRoot/nupkgs"
if ($LASTEXITCODE -ne 0) {
    dotnet tool install --global $packageName --add-source "$RepoRoot/nupkgs"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool install failed' }
}

# --- helpers ---------------------------------------------------------------
function Add-JsonProperty($Object, [string]$Name, $Value) {
    if ($Object.PSObject.Properties[$Name]) { $Object.$Name = $Value }
    else { $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
}

function Merge-JsonFile([string]$Path, [scriptblock]$Merge) {
    $dir = Split-Path $Path -Parent
    if (!(Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $json = if (Test-Path $Path) {
        try { Get-Content $Path -Raw | ConvertFrom-Json }
        catch { throw "Cannot parse $Path (comments/trailing commas are not supported). Please merge the 'mcp-dataverse' entry manually - see skills/ in the repo." }
    } else { New-Object PSObject }
    & $Merge $json | ConvertTo-Json -Depth 20 | Set-Content $Path -Encoding utf8
    Write-Host "    updated $Path"
}

$envBlock = [ordered]@{ DATAVERSE_ENVIRONMENT_URL = $EnvironmentUrl }
if ($AppId) { $envBlock['DATAVERSE_APP_ID'] = $AppId }
foreach ($pair in @(@('AZURE_CLIENT_ID', $ClientId), @('AZURE_TENANT_ID', $TenantId), @('AZURE_CLIENT_SECRET', $ClientSecret))) {
    if ($pair[1]) { $envBlock[$pair[0]] = $pair[1] }
}

# --- 2. OpenCode config ----------------------------------------------------
Write-Host '==> Configuring OpenCode...'
Merge-JsonFile "$HOME/.config/opencode/opencode.json" {
    param($json)
    if (-not $json.mcp) { Add-JsonProperty $json 'mcp' (New-Object PSObject) }
    $entry = New-Object PSObject
    Add-JsonProperty $entry 'type' 'local'
    Add-JsonProperty $entry 'command' @($serverName)
    Add-JsonProperty $entry 'environment' $envBlock
    Add-JsonProperty $json.mcp $serverName $entry
}

# --- 3. Pi via pi-mcp-adapter ----------------------------------------------
Write-Host '==> Configuring Pi (pi-mcp-adapter)...'
if (Get-Command pi -ErrorAction SilentlyContinue) {
    pi install npm:pi-mcp-adapter
    if ($LASTEXITCODE -ne 0) { Write-Warning 'pi install npm:pi-mcp-adapter failed - install it manually.' }
} else {
    Write-Host '    pi not found - skipping adapter install. Install pi, then run: pi install npm:pi-mcp-adapter'
}
Merge-JsonFile "$HOME/.config/mcp/mcp.json" {
    param($json)
    if (-not $json.mcpServers) { Add-JsonProperty $json 'mcpServers' (New-Object PSObject) }
    $entry = New-Object PSObject
    Add-JsonProperty $entry 'command' $serverName
    Add-JsonProperty $entry 'env' $envBlock
    Add-JsonProperty $json.mcpServers $serverName $entry
    # client-side approval on top of the server-side gate (ConfirmWrite)
    if (-not $json.settings) { Add-JsonProperty $json 'settings' (New-Object PSObject) }
    if (-not $json.settings.approveTools) { Add-JsonProperty $json.settings 'approveTools' @() }
    if ($json.settings.approveTools -notcontains '*ConfirmWrite*') {
        Add-JsonProperty $json.settings 'approveTools' ($json.settings.approveTools + '*ConfirmWrite*')
    }
}

# --- 4. Skills (shared dir, read by Pi and OpenCode) ------------------------
Write-Host '==> Installing skills...'
$skillsTarget = Join-Path $HOME '.agents/skills'
New-Item -ItemType Directory -Path $skillsTarget -Force | Out-Null
Copy-Item "$RepoRoot/skills/*" $skillsTarget -Recurse -Force
Write-Host "    copied to $skillsTarget"

Write-Host ''
Write-Host 'Done. Restart OpenCode / Pi - the MCP server starts with the instance.'
Write-Host 'First use triggers the interactive browser login (delegated auth, persistent token cache).'
