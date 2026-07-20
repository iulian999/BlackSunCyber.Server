using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.ServiceProcess;
using System.Text.Json;

namespace BlackSunCyber.Watcher;

/// <summary>
/// Serviciu background care verifică periodic GitHub pentru versiuni noi
/// și actualizează automat BlackSunCyber.Server pe PC-ul Main.
/// </summary>
public class WatcherService : BackgroundService
{
    private readonly ILogger<WatcherService> _logger;
    private readonly WatcherSettings _settings;
    private readonly HttpClient _httpClient;

    // Versiunea curent instalata — citita din version.json la start
    private string _currentVersion = "0.0.0.0000";

    public WatcherService(
        ILogger<WatcherService> logger,
        IOptions<WatcherSettings> settings,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _settings = settings.Value;

        _httpClient = httpClientFactory.CreateClient();
        // GitHub API cere un User-Agent
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "BlackSunCyber-Watcher/1.0");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Entry point
    // ──────────────────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BlackSunWatcher pornit. Verific GitHub la fiecare {Min} minute.",
            _settings.CheckIntervalMinutes);

        // Citim versiunea curenta
        _currentVersion = ReadCurrentVersion();
        _logger.LogInformation("Versiune instalata curent: {V}", _currentVersion);

        // Prima verificare imediat la pornire
        await CheckAndUpdateAsync(stoppingToken);

        // Verificari periodice
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.CheckIntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckAndUpdateAsync(stoppingToken);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Logica principala de verificare + update
    // ──────────────────────────────────────────────────────────────────────────

    private async Task CheckAndUpdateAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("[Watcher] Verific GitHub pentru versiune noua...");

            var release = await GetLatestReleaseAsync(ct);
            if (release is null)
            {
                _logger.LogWarning("[Watcher] Nu am putut obtine informatii de pe GitHub.");
                return;
            }

            // Tag-ul GitHub e "v2026.07.20.1430" — extragem versiunea fara "v"
            var latestVersion = release.TagName.TrimStart('v');

            if (latestVersion == _currentVersion)
            {
                _logger.LogInformation("[Watcher] Versiunea {V} este deja instalata. Nimic de facut.", _currentVersion);
                return;
            }

            _logger.LogInformation("[Watcher] VERSIUNE NOUA detectata: {New} (curent: {Cur}). Incep update-ul...",
                latestVersion, _currentVersion);

            // ── Descarca Server ZIP ────────────────────────────────────────────
            var serverAsset = release.Assets.FirstOrDefault(a =>
                a.Name.StartsWith("BlackSunCyber-Server-", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            var clientAsset = release.Assets.FirstOrDefault(a =>
                a.Name.StartsWith("BlackSunCyber-Client-", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (serverAsset is null)
            {
                _logger.LogError("[Watcher] Nu am gasit fisierul Server ZIP in release-ul {Tag}.", release.TagName);
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "BlackSunUpdate_" + latestVersion);
            Directory.CreateDirectory(tempDir);

            try
            {
                // ── 1. Descarca Server.zip ─────────────────────────────────────
                _logger.LogInformation("[Watcher] Descarc Server ZIP...");
                var serverZipPath = Path.Combine(tempDir, "server.zip");
                await DownloadFileAsync(serverAsset.BrowserDownloadUrl, serverZipPath, ct);

                // ── 2. Descarca Client.zip (pentru distribuire prin LAN) ───────
                if (clientAsset is not null)
                {
                    _logger.LogInformation("[Watcher] Descarc Client ZIP...");
                    var clientZipPath = Path.Combine(tempDir, "client.zip");
                    await DownloadFileAsync(clientAsset.BrowserDownloadUrl, clientZipPath, ct);

                    // Copiaza client.zip in wwwroot/downloads/ ca sa fie disponibil pe LAN
                    Directory.CreateDirectory(_settings.ClientZipDestPath);
                    var destClientZip = Path.Combine(_settings.ClientZipDestPath, "client.zip");
                    File.Copy(clientZipPath, destClientZip, overwrite: true);
                    _logger.LogInformation("[Watcher] client.zip copiat in {Path}", destClientZip);
                }

                // ── 3. Opreste serviciul Windows al serverului ─────────────────
                _logger.LogInformation("[Watcher] Opresc serviciul '{Svc}'...", _settings.ServiceName);
                StopWindowsService(_settings.ServiceName);
                await Task.Delay(2000, ct); // asteptam 2 secunde sa se opreasca complet

                // ── 4. Extrage server.zip peste instalarea existenta ───────────
                _logger.LogInformation("[Watcher] Extrag fisierele noi in {Path}...", _settings.ServerInstallPath);
                await ExtractWithPreserveAsync(serverZipPath, _settings.ServerInstallPath, ct);

                // ── 5. Actualizeaza version.json in wwwroot/downloads/ ─────────
                var versionJson = JsonSerializer.Serialize(new { version = latestVersion });
                var versionDest = Path.Combine(_settings.ClientZipDestPath, "version.json");
                Directory.CreateDirectory(_settings.ClientZipDestPath);
                await File.WriteAllTextAsync(versionDest, versionJson, ct);

                // ── 6. Reporneste serviciul Windows al serverului ──────────────
                _logger.LogInformation("[Watcher] Repornesc serviciul '{Svc}'...", _settings.ServiceName);
                StartWindowsService(_settings.ServiceName);

                // ── 7. Actualizeaza versiunea curenta ──────────────────────────
                _currentVersion = latestVersion;
                _logger.LogInformation("[Watcher] ✅ Update complet! Versiune noua: {V}", _currentVersion);
            }
            finally
            {
                // Curatam fisierele temporare
                try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
            }
        }
        catch (OperationCanceledException)
        {
            // Serviciul se opreste — normal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Watcher] Eroare neasteptata in timpul update-ului.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GitHub API
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{_settings.GitHubOwner}/{_settings.GitHubRepo}/releases/latest";
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Watcher] GitHub API a returnat {Status}", response.StatusCode);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<GitHubRelease>(json);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("[Watcher] Nu ma pot conecta la GitHub: {Msg}", ex.Message);
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers: Download, Extract, Service control
    // ──────────────────────────────────────────────────────────────────────────

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(destPath);
        await stream.CopyToAsync(fileStream, ct);
    }

    private async Task ExtractWithPreserveAsync(string zipPath, string destDir, CancellationToken ct)
    {
        // Backup fisierele protejate
        var backups = new Dictionary<string, string>();
        foreach (var fileName in _settings.PreserveFiles)
        {
            var filePath = Path.Combine(destDir, fileName);
            if (File.Exists(filePath))
            {
                var backupPath = filePath + ".bak";
                File.Copy(filePath, backupPath, overwrite: true);
                backups[filePath] = backupPath;
                _logger.LogDebug("[Watcher] Backup protejat: {F}", fileName);
            }
        }

        // Extrage ZIP (suprascrie tot)
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var destPath = Path.Combine(destDir, entry.FullName);
            var destDirPath = Path.GetDirectoryName(destPath)!;
            Directory.CreateDirectory(destDirPath);

            if (!string.IsNullOrEmpty(entry.Name)) // ignora intrari de tip director
            {
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        // Restaureaza fisierele protejate din backup
        foreach (var (filePath, backupPath) in backups)
        {
            File.Copy(backupPath, filePath, overwrite: true);
            File.Delete(backupPath);
            _logger.LogDebug("[Watcher] Restaurat protejat: {F}", Path.GetFileName(filePath));
        }

        await Task.CompletedTask;
    }

    private void StopWindowsService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Watcher] Nu am putut opri serviciul '{Svc}': {Msg}", serviceName, ex.Message);
        }
    }

    private void StartWindowsService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Watcher] Nu am putut porni serviciul '{Svc}': {Msg}", serviceName, ex.Message);
        }
    }

    private string ReadCurrentVersion()
    {
        var versionFile = Path.Combine(_settings.ClientZipDestPath, "version.json");
        if (!File.Exists(versionFile)) return "0.0.0.0000";

        try
        {
            var json = File.ReadAllText(versionFile);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("version").GetString() ?? "0.0.0.0000";
        }
        catch
        {
            return "0.0.0.0000";
        }
    }
}
