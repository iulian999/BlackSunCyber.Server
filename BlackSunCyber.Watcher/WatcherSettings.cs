using System.Text.Json.Serialization;

namespace BlackSunCyber.Watcher;

/// <summary>
/// Configuratie citita din watchersettings.json
/// </summary>
public class WatcherSettings
{
    public string GitHubOwner { get; set; } = "iulian999";
    public string GitHubRepo { get; set; } = "BlackSunCyber.Server";

    /// <summary>Interval in minute intre verificari GitHub</summary>
    public int CheckIntervalMinutes { get; set; } = 5;

    /// <summary>Calea unde e instalat serverul pe PC Main</summary>
    public string ServerInstallPath { get; set; } = @"C:\BlackSunCyber\Server";

    /// <summary>Calea unde depunem client.zip pentru download local</summary>
    public string ClientZipDestPath { get; set; } = @"C:\BlackSunCyber\Server\wwwroot\downloads";

    /// <summary>Numele Windows Service-ului serverului</summary>
    public string ServiceName { get; set; } = "BlackSunCyber Server";

    /// <summary>Numele executabilului serverului</summary>
    public string ServerExeName { get; set; } = "BlackSunCyber.Server.exe";

    /// <summary>Fisiere care NU trebuie suprascrise la update (ex: appsettings.json cu chei)</summary>
    public List<string> PreserveFiles { get; set; } = ["appsettings.json", "appsettings.Production.json"];
}

/// <summary>
/// Raspuns de la GitHub Releases API
/// </summary>
public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}
