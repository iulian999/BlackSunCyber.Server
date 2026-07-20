# ============================================================
# BlackSunUpdater.ps1
# Ruleaza INAINTEA ClientAgent. Verifica update-uri de pe server.
# Compatible cu orice Windows care are PowerShell 5+
# ============================================================

param()

$Config = @{
    ServerUrl        = "http://192.168.100.102:5000"
    ClientInstallPath = "C:\BlackSunCyber\Client"
    ClientExeName    = "BlackSunCyber.ClientAgent.exe"
    VersionFile      = "version.txt"
    PreserveFiles    = @("agentsettings.json")
}

$LogPath = Join-Path $Config.ClientInstallPath "updater.log"

function Write-Log($msg) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $msg"
    try {
        New-Item -ItemType Directory -Force -Path $Config.ClientInstallPath | Out-Null
        Add-Content -Path $LogPath -Value $line -ErrorAction SilentlyContinue
    } catch {}
}

function Start-Client {
    $exePath = Join-Path $Config.ClientInstallPath $Config.ClientExeName
    if (Test-Path $exePath) {
        Write-Log "Pornesc $($Config.ClientExeName)..."
        Start-Process -FilePath $exePath -WorkingDirectory $Config.ClientInstallPath
        Write-Log "ClientAgent pornit. Updater se inchide."
    } else {
        Write-Log "EROARE: ClientAgent nu a fost gasit la $exePath"
    }
}

Write-Log "=== BlackSunUpdater pornit ==="

try {
    # 1. Citim versiunea locala
    $versionFile = Join-Path $Config.ClientInstallPath $Config.VersionFile
    $localVersion = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { "0.0.0.0000" }
    Write-Log "Versiune locala: $localVersion"

    # 2. Intrebam serverul ce versiune e disponibila (timeout 5 sec)
    try {
        $response = Invoke-RestMethod -Uri "$($Config.ServerUrl)/api/update/version" -TimeoutSec 5 -ErrorAction Stop
        $serverVersion  = $response.version
        $downloadUrl    = $response.clientDownloadUrl
    } catch {
        Write-Log "Nu ma pot conecta la server: $_. Pornesc direct clientul."
        Start-Client
        exit 0
    }

    Write-Log "Versiune pe server: $serverVersion"

    # 3. Comparam
    if ($serverVersion -eq $localVersion) {
        Write-Log "Deja la versiunea actuala. Pornesc clientul."
        Start-Client
        exit 0
    }

    Write-Log "Versiune noua detectata: $serverVersion. Descarc update..."

    # 4. Descarcam ZIP-ul
    $tempZip     = Join-Path $env:TEMP "BlackSunClient_update.zip"
    $tempExtract = Join-Path $env:TEMP "BlackSunClient_extract"

    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -TimeoutSec 60 -ErrorAction Stop
        Write-Log "Download complet. Extrag fisierele..."

        # 5. Backup fisiere protejate
        $backups = @{}
        foreach ($fileName in $Config.PreserveFiles) {
            $src = Join-Path $Config.ClientInstallPath $fileName
            if (Test-Path $src) {
                $bak = "$src.bak"
                Copy-Item $src $bak -Force
                $backups[$src] = $bak
                Write-Log "Backup protejat: $fileName"
            }
        }

        # 6. Extrage ZIP
        if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
        Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force

        # 7. Copiaza fisierele noi
        New-Item -ItemType Directory -Force -Path $Config.ClientInstallPath | Out-Null
        Get-ChildItem $tempExtract -Recurse -File | ForEach-Object {
            $relative = $_.FullName.Substring($tempExtract.Length + 1)
            $dest = Join-Path $Config.ClientInstallPath $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
            Copy-Item $_.FullName $dest -Force
        }

        # 8. Restauram fisierele protejate
        foreach ($entry in $backups.GetEnumerator()) {
            Copy-Item $entry.Value $entry.Key -Force
            Remove-Item $entry.Value -Force
            Write-Log "Restaurat: $(Split-Path $entry.Key -Leaf)"
        }

        # 9. Salvam noua versiune
        Set-Content -Path $versionFile -Value $serverVersion
        Write-Log "Update aplicat! Versiune noua: $serverVersion"

    } finally {
        try { Remove-Item $tempZip -Force -ErrorAction SilentlyContinue } catch {}
        try { Remove-Item $tempExtract -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    }

} catch {
    Write-Log "EROARE critica: $_"
}

# Porneste clientul indiferent de rezultat
Start-Client
