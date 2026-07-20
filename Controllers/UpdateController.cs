using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace BlackSunCyber.Server.Controllers;

/// <summary>
/// Endpoint folosit de BlackSunUpdater.exe de pe PC-urile client.
/// Răspunde cu versiunea curentă a aplicației și URL-ul de download
/// prin rețeaua locală (fără internet necesar pe client).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class UpdateController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<UpdateController> _logger;

    public UpdateController(IWebHostEnvironment env, ILogger<UpdateController> logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/update/version
    /// Returnat de Updater la pornirea fiecărui PC client.
    /// </summary>
    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        // Citim version.json din wwwroot/downloads/version.json
        // Fișierul e depus automat de BlackSunWatcher după fiecare update.
        var versionFilePath = Path.Combine(_env.WebRootPath, "downloads", "version.json");

        if (!System.IO.File.Exists(versionFilePath))
        {
            _logger.LogWarning("version.json nu a fost găsit la {Path}", versionFilePath);
            return Ok(new UpdateInfo
            {
                Version = "0.0.0.0000",
                ClientDownloadUrl = $"{GetBaseUrl()}/downloads/client.zip"
            });
        }

        var json = System.IO.File.ReadAllText(versionFilePath);
        var versionInfo = System.Text.Json.JsonSerializer.Deserialize<VersionFile>(json);

        return Ok(new UpdateInfo
        {
            Version = versionInfo?.Version ?? "0.0.0.0000",
            ClientDownloadUrl = $"{GetBaseUrl()}/downloads/client.zip"
        });
    }

    /// <summary>
    /// GET /api/update/ping  — folosit de Watcher pentru health check
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private string GetBaseUrl()
    {
        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}";
    }
}

// ─── Modele locale (nu trebuie puse în Models/ pentru că sunt mici) ──────────

public class UpdateInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.0.0.0000";

    [JsonPropertyName("clientDownloadUrl")]
    public string ClientDownloadUrl { get; set; } = string.Empty;
}

internal class VersionFile
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
