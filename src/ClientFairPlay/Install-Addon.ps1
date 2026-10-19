#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$Target,[ValidateSet('Install','Verify','Rollback')][string]$Action='Install',[string]$Journal,[switch]$Apply)
$ErrorActionPreference='Stop'
try {
    $python=(Get-Command python -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $arguments=@('-B',(Join-Path $PSScriptRoot 'tools/install_addon.py'),$Action.ToLowerInvariant(),'--target',$Target)
    if($Journal){$arguments+=@('--journal',$Journal)}
    if($Apply -and $PSCmdlet.ShouldProcess($Target,'Install or roll back the pinned Fair Play QA addon')){$arguments+='--apply'}
    & $python @arguments
    exit $LASTEXITCODE
} catch { Write-Error $_ -ErrorAction Continue; exit 2 }
