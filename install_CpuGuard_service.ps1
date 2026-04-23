[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ServiceName = "CpuGuardService",

    [Parameter(Mandatory = $false)]
    [string]$ExePath = "C:\CpuGuard\CpuGuard.Service.exe",

    [Parameter(Mandatory = $false)]
    [string]$Description = "Monitors Windrose server activity and applies/removes Job Object CPU cap.",

    [Parameter(Mandatory = $false)]
    [ValidateSet("LocalSystem", "LocalService", "NetworkService")]
    [string]$Account = "LocalSystem",

    [Parameter(Mandatory = $false)]
    [switch]$SkipStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Sc {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & sc.exe @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "sc.exe failed (exit code $exitCode): sc.exe $($Arguments -join ' ')"
    }
}

Write-Host "Installing service '$ServiceName'..." -ForegroundColor Cyan

if (-not (Test-IsAdmin)) {
    throw "This script must be run in an elevated PowerShell session (Run as Administrator)."
}

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Service executable not found: $ExePath"
}

$quotedExe = '"' + $ExePath + '"'

# Best-effort cleanup of existing service.
& sc.exe stop $ServiceName 2>$null | Out-Null
& sc.exe delete $ServiceName 2>$null | Out-Null
Start-Sleep -Seconds 1

Invoke-Sc -Arguments @("create", $ServiceName, "binPath=", $quotedExe, "start=", "auto", "obj=", $Account)
Invoke-Sc -Arguments @("description", $ServiceName, $Description)
Invoke-Sc -Arguments @("failure", $ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/5000")

if (-not $SkipStart) {
    Invoke-Sc -Arguments @("start", $ServiceName)
}

Write-Host ""
Write-Host "Service configuration:" -ForegroundColor Green
& sc.exe qc $ServiceName

Write-Host ""
Write-Host "Service state:" -ForegroundColor Green
& sc.exe query $ServiceName

Write-Host ""
Write-Host "Done." -ForegroundColor Green
