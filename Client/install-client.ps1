# ============================================================================
# install-client.ps1 - v2
# Ruleaza ODATA pe fiecare PC client (calculatoarele gamerilor)
# Necesita: PowerShell ca Administrator
#
# Ce face:
#   1. Creeaza folderul C:\BlackSunCyber\Client\
#   2. Copiaza BlackSunUpdater.ps1 acolo
#   3. Adauga la startup Windows (Task Scheduler) - ruleaza la fiecare logon
#   4. Copiaza agentsettings.json cu StationId-ul corect
# ============================================================================

#Requires -RunAsAdministrator

param(
    [Parameter(Mandatory=$true)]
    [int]$StationId
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  BlackSunCyber - Instalare Client (Statie $StationId)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ── Configuratie ─────────────────────────────────────────────────────────────
$ClientInstallPath = "C:\BlackSunCyber\Client"
$ServerUrl         = "http://192.168.100.102:5000"
$StartupTaskName   = "BlackSunUpdater_Station$StationId"

# Gaseste scriptul sursa langa install-client.ps1
$ScriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$UpdaterScriptSrc  = "$ScriptDir\BlackSunUpdater.ps1"
$UpdaterScriptDest = "$ClientInstallPath\BlackSunUpdater.ps1"

# ── PASUL 1: Verifica daca BlackSunUpdater.ps1 exista ────────────────────────
Write-Host "[1/4] Verific fisierele..." -ForegroundColor Yellow

if (-not (Test-Path $UpdaterScriptSrc)) {
    Write-Host ""
    Write-Host "EROARE: Nu gasesc BlackSunUpdater.ps1 langa script." -ForegroundColor Red
    Write-Host "Asigura-te ca BlackSunUpdater.ps1 e in acelasi folder cu install-client.ps1" -ForegroundColor Red
    exit 1
}
Write-Host "      OK: Fisierele sunt prezente." -ForegroundColor Green

# ── PASUL 2: Creeaza folderul si copiaza fisierele ───────────────────────────
Write-Host "[2/4] Copiez fisierele in $ClientInstallPath..." -ForegroundColor Yellow

New-Item -ItemType Directory -Force -Path $ClientInstallPath | Out-Null
Copy-Item -Path $UpdaterScriptSrc -Destination $UpdaterScriptDest -Force
Write-Host "      OK: BlackSunUpdater.ps1 copiat." -ForegroundColor Green

# Creeaza agentsettings.json cu StationId-ul corect
$agentSettingsPath = "$ClientInstallPath\agentsettings.json"
if (-not (Test-Path $agentSettingsPath)) {
    @{ StationId = $StationId; ServerUrl = $ServerUrl; AccessToken = "" } | ConvertTo-Json | Set-Content -Path $agentSettingsPath -Encoding UTF8
    Write-Host "      OK: agentsettings.json creat pentru Statia $StationId." -ForegroundColor Green
} else {
    Write-Host "      INFO: agentsettings.json deja exista - nu il suprascriem." -ForegroundColor DarkYellow
}

# ── PASUL 3: Task Scheduler - pornire automata la logon ──────────────────────
Write-Host "[3/4] Configurez pornire automata la boot..." -ForegroundColor Yellow

Unregister-ScheduledTask -TaskName $StartupTaskName -Confirm:$false -ErrorAction SilentlyContinue

# Actiunea: powershell.exe -WindowStyle Hidden -File "C:\BlackSunCyber\Client\BlackSunUpdater.ps1"
$Action = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-ExecutionPolicy Bypass -WindowStyle Hidden -File `"$UpdaterScriptDest`"" `
    -WorkingDirectory $ClientInstallPath

$Trigger  = New-ScheduledTaskTrigger -AtLogOn
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

# ── PASUL 4: Finalizare ──────────────────────────────────────────────────────
Write-Host "[4/4] Instalare finalizata!" -ForegroundColor Yellow
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  INSTALARE COMPLETA - Statie $StationId" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "La urmatoarea pornire a PC-ului:" -ForegroundColor White
Write-Host "  1. BlackSunUpdater.ps1 va porni automat (invizibil)" -ForegroundColor White
Write-Host "  2. Va verifica daca exista versiune noua pe server ($ServerUrl)" -ForegroundColor White
Write-Host "  3. Daca da, descarca si instaleaza (< 1 secunda pe LAN)" -ForegroundColor White
Write-Host "  4. Porneste BlackSunCyber.ClientAgent.exe" -ForegroundColor White
Write-Host ""
Write-Host "Log-uri updater: $ClientInstallPath\updater.log" -ForegroundColor DarkGray
Write-Host ""

# Test direct - fara Start-Process, ruleaza scriptul inline
$testNow = Read-Host "Vrei sa testezi acum? (da/nu)"
if ($testNow -eq "da") {
    Write-Host "Pornesc BlackSunUpdater pentru test..." -ForegroundColor Cyan
    # Executam scriptul direct in sesiunea curenta ca sa vedem output-ul
    & powershell.exe -ExecutionPolicy Bypass -WindowStyle Normal -File $UpdaterScriptDest
    Write-Host ""
    Write-Host "=== Log updater ===" -ForegroundColor Cyan
    if (Test-Path "$ClientInstallPath\updater.log") {
        Get-Content "$ClientInstallPath\updater.log" -Tail 10
    }
}
