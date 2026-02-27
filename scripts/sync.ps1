#Requires -Version 5.1
<#
.SYNOPSIS
    Sincronización automática bidireccional entre carpeta local y repositorio GitHub.
    Automatic two-way sync between local folder and GitHub repository.

.DESCRIPTION
    Este script vigila la carpeta del repositorio local.
    - Cada vez que detecta un cambio local → hace git add / commit / push.
    - Cada N segundos comprueba si hay cambios remotos → hace git pull.
    Lanza una ventana de consola con el log de actividad.

    This script watches the local repo folder.
    - Whenever it detects a local change  → runs git add / commit / push.
    - Every N seconds it checks for remote changes → runs git pull.
    A console window shows a live activity log.

.PARAMETER RepoPath
    Ruta a la carpeta raíz del repositorio local.
    Path to the local repository root folder.
    Default: the folder that contains this script's parent.

.PARAMETER PollSeconds
    Interval (seconds) between remote-pull checks. Default: 60.

.PARAMETER CommitMessage
    Prefix for auto-commit messages. Default: "auto-sync".

.EXAMPLE
    # Run from the repo root:
    .\scripts\sync.ps1

    # Run with a custom poll interval and repo path:
    .\scripts\sync.ps1 -RepoPath "C:\Projects\setups" -PollSeconds 30
#>
param(
    [string]$RepoPath     = (Split-Path -Parent $PSScriptRoot),
    [int]   $PollSeconds  = 60,
    [string]$CommitMessage = "auto-sync"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── helpers ──────────────────────────────────────────────────────────────────
function Write-Log {
    param([string]$Msg, [string]$Level = "INFO")
    $ts  = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "OK"   { "Green"  }
        "WARN" { "Yellow" }
        "ERR"  { "Red"    }
        default{ "Cyan"   }
    }
    Write-Host "[$ts][$Level] $Msg" -ForegroundColor $color
}

function Invoke-Git {
    param([string[]]$Args)
    $result = & git -C $RepoPath @Args 2>&1
    return $result
}

function Get-RemoteHead {
    $out = & git -C $RepoPath ls-remote origin HEAD 2>&1
    if ($LASTEXITCODE -eq 0) { return ($out -split "\s+")[0] }
    return $null
}

function Get-LocalHead {
    return (& git -C $RepoPath rev-parse HEAD 2>&1).Trim()
}

# ── validate repo ─────────────────────────────────────────────────────────────
if (-not (Test-Path (Join-Path $RepoPath ".git"))) {
    Write-Log "No se encontró .git en '$RepoPath'. Comprueba la ruta." "ERR"
    Write-Log "'.git' not found in '$RepoPath'. Check the path." "ERR"
    exit 1
}

Write-Log "Repo : $RepoPath"
Write-Log "Poll : cada $PollSeconds s / every $PollSeconds s"
Write-Log "Rama activa / Active branch: $(Invoke-Git 'branch','--show-current')"
Write-Log "Iniciando sincronización… / Starting sync…" "OK"

# ── FileSystemWatcher — detecta cambios locales ───────────────────────────────
$watcher                    = New-Object System.IO.FileSystemWatcher
$watcher.Path               = $RepoPath
$watcher.IncludeSubdirectories = $true
$watcher.NotifyFilter       = [System.IO.NotifyFilters]'FileName,DirectoryName,LastWrite'
$watcher.EnableRaisingEvents= $false   # we use WaitForChanged in the loop

# Patterns to ignore (build artifacts, git internals, IDE caches)
$ignorePatterns = @(
    '\.git[\\/]',
    '[\\/]bin[\\/]',
    '[\\/]obj[\\/]',
    '[\\/]\.vs[\\/]',
    '[\\/]packages[\\/]',
    '~\$',
    '\.user$'
)

function Should-Ignore([string]$path) {
    foreach ($p in $ignorePatterns) {
        if ($path -match $p) { return $true }
    }
    return $false
}

# ── push pending local changes ────────────────────────────────────────────────
function Push-LocalChanges {
    $status = Invoke-Git "status","--porcelain"
    if (-not $status) { return }

    # Filter out ignored paths reported by git status
    $relevant = $status | Where-Object {
        $line = $_.Trim()
        $file = if ($line.Length -gt 3) { $line.Substring(3) } else { "" }
        -not (Should-Ignore $file)
    }
    if (-not $relevant) { return }

    Write-Log "Cambios locales detectados / Local changes detected:" "WARN"
    $relevant | ForEach-Object { Write-Log "  $_" }

    Invoke-Git "add","--all" | Out-Null

    $ts  = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $msg = "$CommitMessage [$ts]"
    $out = Invoke-Git "commit","-m",$msg
    Write-Log "Commit: $($out -join ' ')"

    $out = Invoke-Git "push"
    if ($LASTEXITCODE -eq 0) {
        Write-Log "Push exitoso / Push succeeded." "OK"
    } else {
        Write-Log "Push falló / Push failed: $($out -join ' ')" "ERR"
    }
}

# ── pull remote changes ───────────────────────────────────────────────────────
function Pull-RemoteChanges {
    $remote = Get-RemoteHead
    $local  = Get-LocalHead
    if ($remote -and ($remote -ne $local)) {
        Write-Log "Cambios remotos detectados / Remote changes detected. Pulling…" "WARN"
        $out = Invoke-Git "pull","--rebase"
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Pull exitoso / Pull succeeded." "OK"
        } else {
            Write-Log "Pull falló / Pull failed: $($out -join ' ')" "ERR"
        }
    }
}

# ── main loop ─────────────────────────────────────────────────────────────────
$lastPoll = [datetime]::MinValue

Write-Log "Vigilando carpeta / Watching folder: $RepoPath"
Write-Log "Presiona Ctrl+C para detener / Press Ctrl+C to stop."

$watcher.EnableRaisingEvents = $true

try {
    while ($true) {
        # Non-blocking check for file-system events (500 ms window)
        $change = $watcher.WaitForChanged([System.IO.WatcherChangeTypes]::All, 500)

        if (-not $change.TimedOut) {
            if (-not (Should-Ignore $change.Name)) {
                Write-Log "Cambio local: $($change.Name)" "WARN"
                Start-Sleep -Milliseconds 500    # debounce — wait for saves to settle
                Push-LocalChanges
            }
        }

        # Periodic remote pull
        if (([datetime]::Now - $lastPoll).TotalSeconds -ge $PollSeconds) {
            Pull-RemoteChanges
            $lastPoll = [datetime]::Now
        }
    }
}
finally {
    $watcher.EnableRaisingEvents = $false
    $watcher.Dispose()
    Write-Log "Sincronización detenida / Sync stopped."
}
