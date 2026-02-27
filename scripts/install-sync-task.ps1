#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Registra (o elimina) una Tarea Programada de Windows que lanza sync.ps1
    al iniciar sesión, de modo que la sincronización arranca sola.

    Registers (or removes) a Windows Scheduled Task that launches sync.ps1
    at logon so that sync starts automatically.

.PARAMETER Uninstall
    Si se especifica, elimina la tarea en lugar de crearla.
    If specified, removes the task instead of creating it.

.PARAMETER RepoPath
    Ruta al repositorio local. Por defecto: carpeta padre de este script.
    Path to the local repository. Default: parent folder of this script.

.PARAMETER PollSeconds
    Intervalo (segundos) entre comprobaciones remotas. Por defecto: 60.
    Interval (seconds) between remote checks. Default: 60.

.EXAMPLE
    # Instalar (ejecutar como Administrador):
    .\scripts\install-sync-task.ps1

    # Desinstalar:
    .\scripts\install-sync-task.ps1 -Uninstall
#>
param(
    [switch]$Uninstall,
    [string]$RepoPath    = (Split-Path -Parent $PSScriptRoot),
    [int]   $PollSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$TaskName    = "AvoSetups-GitSync"
$SyncScript  = Join-Path $PSScriptRoot "sync.ps1"

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Tarea eliminada / Task removed: $TaskName" -ForegroundColor Green
    } else {
        Write-Host "La tarea no existe / Task not found: $TaskName" -ForegroundColor Yellow
    }
    exit 0
}

# ── validate ──────────────────────────────────────────────────────────────────
if (-not (Test-Path $SyncScript)) {
    Write-Host "No se encontró sync.ps1 en '$SyncScript'." -ForegroundColor Red
    Write-Host "Asegúrate de ejecutar este script desde la carpeta del repo." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path (Join-Path $RepoPath ".git"))) {
    Write-Host "No se encontró .git en '$RepoPath'." -ForegroundColor Red
    Write-Host "Comprueba que RepoPath apunta a la raíz del repositorio." -ForegroundColor Red
    exit 1
}

# ── build task ────────────────────────────────────────────────────────────────
$pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue)?.Source
if (-not $pwsh) { $pwsh = "powershell.exe" }

# Launch sync.ps1 in a minimized window
$action  = New-ScheduledTaskAction `
    -Execute $pwsh `
    -Argument "-NonInteractive -WindowStyle Minimized -ExecutionPolicy Bypass -File `"$SyncScript`" -RepoPath `"$RepoPath`" -PollSeconds $PollSeconds"

# Trigger: at logon of the current user
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME

$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit (New-TimeSpan -Hours 0) `   # no time limit
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask `
    -TaskName    $TaskName `
    -Action      $action `
    -Trigger     $trigger `
    -Settings    $settings `
    -Description "Auto-sync del repositorio de setups con GitHub / Auto-sync setups repo with GitHub" `
    -RunLevel    Limited `
    -Force | Out-Null

Write-Host ""
Write-Host "✔  Tarea registrada / Task registered: $TaskName" -ForegroundColor Green
Write-Host "   Se ejecutará al iniciar sesión / Runs at logon."
Write-Host "   Repo : $RepoPath"
Write-Host "   Poll : $PollSeconds s"
Write-Host ""
Write-Host "Para iniciarla ahora sin reiniciar / To start it now without restarting:"
Write-Host "   Start-ScheduledTask -TaskName '$TaskName'"
Write-Host ""
Write-Host "Para eliminarla / To remove it:"
Write-Host "   .\scripts\install-sync-task.ps1 -Uninstall"
