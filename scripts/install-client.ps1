# ============================================================================
# install-client.ps1
# Ruleaza ODATA pe fiecare PC client (calculatoarele gamerilor)
# Necesita: PowerShell ca Administrator
#
# Ce face:
#   1. Creeaza folderul C:\BlackSunCyber\Client\
#   2. Copiaza BlackSunUpdater.exe acolo
#   3. Adauga BlackSunUpdater.exe la startup Windows
#      (se lanseaza automat la fiecare pornire a PC-ului)
#   4. Copiaza agentsettings.json cu StationId-ul corect
# ============================================================================

#Requires -RunAsAdministrator

param(
    # ID-ul statiei (1, 2, 3, etc.) - TREBUIE specificat la rulare
    # Exemplu: .\install-client.ps1 -StationId 3
    [Parameter(Mandatory=$true)]
    [int]$StationId
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  BlackSunCyber - Instalare Client (Statie $StationId)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ── Configuratie ────────────────────────────────────────────────────────────
$ClientInstallPath  = "C:\BlackSunCyber\Client"
$ServerUrl          = "http://192.168.100.102:5000"

# Cauta BlackSunUpdater.exe in acelasi folder cu scriptul
$ScriptDir          = Split-Path -Parent $MyInvocation.MyCommand.Path
$UpdaterSource      = "$ScriptDir\BlackSunUpdater.exe"
$UpdaterDest        = "$ClientInstallPath\BlackSunUpdater.exe"

$StartupTaskName    = "BlackSunUpdater_Station$StationId"

# ── PASUL 1: Verifica daca Updater-ul exista ────────────────────────────────
Write-Host "[1/4] Verific fisierele..." -ForegroundColor Yellow

if (-not (Test-Path $UpdaterSource)) {
    Write-Host ""
    Write-Host "EROARE: Nu gasesc BlackSunUpdater.exe langa script." -ForegroundColor Red
    Write-Host "Asigura-te ca BlackSunUpdater.exe e in acelasi folder cu install-client.ps1" -ForegroundColor Red
    exit 1
}

# ── PASUL 2: Creeaza folderul si copiaza fisierele ──────────────────────────
Write-Host "[2/4] Copiez fisierele in $ClientInstallPath..." -ForegroundColor Yellow

New-Item -ItemType Directory -Force -Path $ClientInstallPath | Out-Null
Copy-Item -Path $UpdaterSource -Destination $UpdaterDest -Force

# Copiaza updatersettings.json daca exista langa script
$UpdaterSettingsSrc = "$ScriptDir\updatersettings.json"
if (Test-Path $UpdaterSettingsSrc) {
    Copy-Item -Path $UpdaterSettingsSrc -Destination "$ClientInstallPath\updatersettings.json" -Force
    Write-Host "      OK: updatersettings.json copiat." -ForegroundColor Green
}

# Creeaza agentsettings.json cu StationId-ul corect pentru aceasta statie
$AgentSettings = @{
    StationId   = $StationId
    ServerUrl   = $ServerUrl
    AccessToken = ""
} | ConvertTo-Json

$agentSettingsPath = "$ClientInstallPath\agentsettings.json"
if (-not (Test-Path $agentSettingsPath)) {
    # Scriem doar daca nu exista deja (nu suprascriem token-ul deja setat)
    $AgentSettings | Set-Content -Path $agentSettingsPath -Encoding UTF8
    Write-Host "      OK: agentsettings.json creat pentru Statia $StationId." -ForegroundColor Green
} else {
    Write-Host "      INFO: agentsettings.json deja exista - nu il suprascriem." -ForegroundColor DarkYellow
}

Write-Host "      OK: Fisierele copiate." -ForegroundColor Green

# ── PASUL 3: Adauga la startup Windows (Task Scheduler) ────────────────────
Write-Host "[3/4] Configurez pornire automata la boot..." -ForegroundColor Yellow

# Sterge task vechi daca exista
Unregister-ScheduledTask -TaskName $StartupTaskName -Confirm:$false -ErrorAction SilentlyContinue

# Creeaza task nou care porneste BlackSunUpdater la logon
$Action = New-ScheduledTaskAction -Execute $UpdaterDest -WorkingDirectory $ClientInstallPath
$Trigger = New-ScheduledTaskTrigger -AtLogOn
$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 2) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)
$Principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest

Register-ScheduledTask `
    -TaskName $StartupTaskName `
    -Action $Action `
    -Trigger $Trigger `
    -Settings $Settings `
    -Principal $Principal `
    -Description "BlackSunCyber Updater - verifica update la pornire, apoi lanseaza ClientAgent" | Out-Null

Write-Host "      OK: Task Scheduler configurat." -ForegroundColor Green

# ── PASUL 4: Testeaza (optiona - ruleaza updater-ul acum) ───────────────────
Write-Host "[4/4] Instalare completa!" -ForegroundColor Yellow

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  INSTALARE COMPLETA - Statie $StationId" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "La urmatoarea pornire a PC-ului:" -ForegroundColor White
Write-Host "  1. BlackSunUpdater.exe va porni automat" -ForegroundColor White
Write-Host "  2. Va verifica daca exista versiune noua pe server ($ServerUrl)" -ForegroundColor White
Write-Host "  3. Daca da, descarca si instaleaza (< 1 secunda pe LAN)" -ForegroundColor White
Write-Host "  4. Porneste BlackSunCyber.ClientAgent.exe" -ForegroundColor White
Write-Host ""
Write-Host "Log-uri updater: $ClientInstallPath\updater.log" -ForegroundColor DarkGray
Write-Host ""

$testNow = Read-Host "Vrei sa testezi acum? (da/nu)"
if ($testNow -eq "da") {
    Write-Host "Pornesc BlackSunUpdater pentru test..." -ForegroundColor Cyan
    Start-Process -FilePath $UpdaterDest -WorkingDirectory $ClientInstallPath
    Write-Host "Gata! Verifica $ClientInstallPath\updater.log pentru detalii." -ForegroundColor Green
}
