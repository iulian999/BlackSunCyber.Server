# ============================================================================
# install-server.ps1
# Ruleaza ODATA pe PC-ul Main (serverul de la sala)
# Necesita: PowerShell ca Administrator
#
# Ce face:
#   1. Creeaza folderele de instalare
#   2. Instaleaza BlackSunCyber.Server ca Windows Service
#   3. Instaleaza BlackSunCyber.Watcher ca Windows Service
#   4. Ambele pornesc automat la fiecare boot
# ============================================================================

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  BlackSunCyber - Instalare Server + Watcher" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ── Configuratie ────────────────────────────────────────────────────────────
$ServerInstallPath  = "C:\BlackSunCyber\Server"
$WatcherInstallPath = "C:\BlackSunCyber\Watcher"
$DownloadsPath      = "$ServerInstallPath\wwwroot\downloads"

$ServerServiceName  = "BlackSunCyber Server"
$WatcherServiceName = "BlackSunCyber Watcher"

$ServerExe          = "$ServerInstallPath\BlackSunCyber.Server.exe"
$WatcherExe         = "$WatcherInstallPath\BlackSunWatcher.exe"

# ── PASUL 1: Creeaza folderele ───────────────────────────────────────────────
Write-Host "[1/6] Creez folderele de instalare..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $ServerInstallPath  | Out-Null
New-Item -ItemType Directory -Force -Path $WatcherInstallPath | Out-Null
New-Item -ItemType Directory -Force -Path $DownloadsPath       | Out-Null
Write-Host "      OK: $ServerInstallPath" -ForegroundColor Green
Write-Host "      OK: $WatcherInstallPath" -ForegroundColor Green

# ── PASUL 2: Verifica daca fisierele exista ──────────────────────────────────
Write-Host "[2/6] Verific fisierele de instalare..." -ForegroundColor Yellow

if (-not (Test-Path $ServerExe)) {
    Write-Host ""
    Write-Host "EROARE: Nu gasesc $ServerExe" -ForegroundColor Red
    Write-Host "Copiaza mai intai fisierele build din publish/server/ in $ServerInstallPath" -ForegroundColor Red
    Write-Host "Sau ruleaza scriptul dupa primul push pe GitHub (build automat)." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $WatcherExe)) {
    Write-Host ""
    Write-Host "EROARE: Nu gasesc $WatcherExe" -ForegroundColor Red
    Write-Host "Copiaza fisierele din publish/watcher/ in $WatcherInstallPath" -ForegroundColor Red
    exit 1
}

Write-Host "      OK: Fisierele sunt prezente." -ForegroundColor Green

# ── PASUL 3: Dezinstaleaza servicii vechi (daca exista) ─────────────────────
Write-Host "[3/6] Curatare servicii vechi (daca exista)..." -ForegroundColor Yellow

foreach ($svcName in @($ServerServiceName, $WatcherServiceName)) {
    $existing = Get-Service -Name $svcName -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -eq 'Running') {
            Stop-Service -Name $svcName -Force
            Start-Sleep -Seconds 2
        }
        sc.exe delete $svcName | Out-Null
        Write-Host "      Sters serviciu vechi: $svcName" -ForegroundColor DarkYellow
    }
}

# ── PASUL 4: Instaleaza BlackSunCyber.Server ca Windows Service ─────────────
Write-Host "[4/6] Instalez BlackSunCyber Server ca Windows Service..." -ForegroundColor Yellow

New-Service `
    -Name $ServerServiceName `
    -BinaryPathName $ServerExe `
    -DisplayName "BlackSunCyber Server" `
    -Description "Server principal BlackSunCyber - API, SignalR, web admin" `
    -StartupType Automatic | Out-Null

# Seteaza sa reporneasca automat dupa crash
sc.exe failure $ServerServiceName reset= 60 actions= restart/5000/restart/10000/restart/30000 | Out-Null

Write-Host "      OK: Serviciu Server instalat." -ForegroundColor Green

# ── PASUL 5: Instaleaza BlackSunCyber.Watcher ca Windows Service ────────────
Write-Host "[5/6] Instalez BlackSunCyber Watcher ca Windows Service..." -ForegroundColor Yellow

New-Service `
    -Name $WatcherServiceName `
    -BinaryPathName $WatcherExe `
    -DisplayName "BlackSunCyber Watcher (Auto-Update)" `
    -Description "Verifica GitHub pentru versiuni noi si actualizeaza automat serverul" `
    -StartupType Automatic | Out-Null

sc.exe failure $WatcherServiceName reset= 60 actions= restart/5000/restart/10000/restart/30000 | Out-Null

Write-Host "      OK: Serviciu Watcher instalat." -ForegroundColor Green

# ── PASUL 6: Porneste ambele servicii ───────────────────────────────────────
Write-Host "[6/6] Pornesc serviciile..." -ForegroundColor Yellow

Start-Service -Name $ServerServiceName
Start-Sleep -Seconds 2
Start-Service -Name $WatcherServiceName

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  INSTALARE COMPLETA!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Server:  http://localhost:5000" -ForegroundColor Cyan
Write-Host "Admin:   http://localhost:5000/admin" -ForegroundColor Cyan
Write-Host ""
Write-Host "Servicii instalate:" -ForegroundColor White
Get-Service -Name $ServerServiceName, $WatcherServiceName | Format-Table Name, Status, StartType
