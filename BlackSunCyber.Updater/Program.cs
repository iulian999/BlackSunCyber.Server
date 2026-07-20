using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

// ──────────────────────────────────────────────────────────────────────────────
// BlackSunUpdater — porneste INAINTEA ClientAgent-ului
// Verifica daca exista o versiune noua pe serverul local, descarca si aplica.
// Dureaza < 1 secunda pe reteaua locala daca nu e update disponibil.
// ──────────────────────────────────────────────────────────────────────────────

var config = LoadConfig();
var log = new SimpleLog(Path.Combine(config.ClientInstallPath, "updater.log"));

log.Write("=== BlackSunUpdater pornit ===");

try
{
    // ── 1. Citim versiunea instalata local ────────────────────────────────────
    var versionFilePath = Path.Combine(config.ClientInstallPath, config.VersionFile);
    var localVersion = File.Exists(versionFilePath)
        ? File.ReadAllText(versionFilePath).Trim()
        : "0.0.0.0000";

    log.Write($"Versiune locala: {localVersion}");

    // ── 2. Intrebam serverul local ce versiune e disponibila ──────────────────
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    UpdateInfo? updateInfo = null;

    try
    {
        var response = await http.GetStringAsync($"{config.ServerUrl}/api/update/version");
        updateInfo = JsonSerializer.Deserialize<UpdateInfo>(response);
    }
    catch (Exception ex)
    {
        log.Write($"Nu ma pot conecta la server: {ex.Message}. Pornesc direct clientul.");
        LaunchClient(config, log);
        return;
    }

    if (updateInfo is null || string.IsNullOrEmpty(updateInfo.Version))
    {
        log.Write("Raspuns invalid de la server. Pornesc direct clientul.");
        LaunchClient(config, log);
        return;
    }

    log.Write($"Versiune pe server: {updateInfo.Version}");

    // ── 3. Comparam versiunile ────────────────────────────────────────────────
    if (updateInfo.Version == localVersion)
    {
        log.Write("Deja la versiunea actuala. Pornesc clientul.");
        LaunchClient(config, log);
        return;
    }

    // ── 4. Versiune noua — descarcam client.zip de pe LAN ────────────────────
    log.Write($"Versiune noua detectata: {updateInfo.Version}. Descarc update-ul...");

    var tempZip = Path.Combine(Path.GetTempPath(), "BlackSunClient_update.zip");
    var tempExtract = Path.Combine(Path.GetTempPath(), "BlackSunClient_extract");

    try
    {
        // Descarca (va fi foarte rapid — reteaua locala)
        using var downloadResponse = await http.GetAsync(
            updateInfo.ClientDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);
        downloadResponse.EnsureSuccessStatusCode();

        await using var stream = await downloadResponse.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(tempZip);
        await stream.CopyToAsync(fileStream);

        log.Write("Download complet. Extrag fisierele...");

        // ── 5. Backup fisierele protejate (agentsettings.json are StationId unic) ──
        var backups = new Dictionary<string, string>();
        foreach (var fileName in config.PreserveFiles)
        {
            var src = Path.Combine(config.ClientInstallPath, fileName);
            if (File.Exists(src))
            {
                var bak = src + ".bak";
                File.Copy(src, bak, overwrite: true);
                backups[src] = bak;
                log.Write($"Backup protejat: {fileName}");
            }
        }

        // ── 6. Extrage ZIP ────────────────────────────────────────────────────
        if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, recursive: true);
        ZipFile.ExtractToDirectory(tempZip, tempExtract);

        // Copiaza fisierele noi peste instalarea existenta
        Directory.CreateDirectory(config.ClientInstallPath);
        foreach (var file in Directory.GetFiles(tempExtract, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(tempExtract, file);
            var dest = Path.Combine(config.ClientInstallPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        // ── 7. Restaureaza fisierele protejate ────────────────────────────────
        foreach (var (filePath, backupPath) in backups)
        {
            File.Copy(backupPath, filePath, overwrite: true);
            File.Delete(backupPath);
            log.Write($"Restaurat: {Path.GetFileName(filePath)}");
        }

        // ── 8. Salveaza noua versiune ─────────────────────────────────────────
        await File.WriteAllTextAsync(versionFilePath, updateInfo.Version);
        log.Write($"✅ Update aplicat! Versiune noua: {updateInfo.Version}");
    }
    finally
    {
        // Curatam fisierele temporare
        try { File.Delete(tempZip); } catch { }
        try { Directory.Delete(tempExtract, recursive: true); } catch { }
    }

    // ── 9. Porneste clientul actualizat ───────────────────────────────────────
    LaunchClient(config, log);
}
catch (Exception ex)
{
    log.Write($"EROARE critica: {ex}");
    // Chiar daca updater-ul da eroare, incercam sa pornim clientul
    LaunchClient(config, log);
}

// ──────────────────────────────────────────────────────────────────────────────
// Helper functions
// ──────────────────────────────────────────────────────────────────────────────

static void LaunchClient(UpdaterSettings cfg, SimpleLog log)
{
    var clientExePath = Path.Combine(cfg.ClientInstallPath, cfg.ClientExeName);
    if (!File.Exists(clientExePath))
    {
        log.Write($"EROARE: ClientAgent nu a fost gasit la {clientExePath}");
        return;
    }

    log.Write($"Pornesc {cfg.ClientExeName}...");
    var psi = new ProcessStartInfo
    {
        FileName = clientExePath,
        UseShellExecute = true,
        WorkingDirectory = cfg.ClientInstallPath
    };
    Process.Start(psi);
    log.Write("ClientAgent pornit. Updater se inchide.");
}

static UpdaterSettings LoadConfig()
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "updatersettings.json");
    if (!File.Exists(configPath)) return new UpdaterSettings();

    try
    {
        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<UpdaterSettings>(json) ?? new UpdaterSettings();
    }
    catch
    {
        return new UpdaterSettings();
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Modele
// ──────────────────────────────────────────────────────────────────────────────

public class UpdaterSettings
{
    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = "http://192.168.100.102:5000";

    [JsonPropertyName("clientInstallPath")]
    public string ClientInstallPath { get; set; } = @"C:\BlackSunCyber\Client";

    [JsonPropertyName("clientExeName")]
    public string ClientExeName { get; set; } = "BlackSunCyber.ClientAgent.exe";

    [JsonPropertyName("versionFile")]
    public string VersionFile { get; set; } = "version.txt";

    [JsonPropertyName("preserveFiles")]
    public List<string> PreserveFiles { get; set; } = ["agentsettings.json"];
}

public class UpdateInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("clientDownloadUrl")]
    public string ClientDownloadUrl { get; set; } = string.Empty;
}

// ──────────────────────────────────────────────────────────────────────────────
// Logger simplu in fisier (nu avem ILogger disponibil in console app minimal)
// ──────────────────────────────────────────────────────────────────────────────

public class SimpleLog(string logPath)
{
    private readonly string _logPath = logPath;
    private readonly object _lock = new();

    public void Write(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch { /* log failure is not critical */ }
        }
    }
}
