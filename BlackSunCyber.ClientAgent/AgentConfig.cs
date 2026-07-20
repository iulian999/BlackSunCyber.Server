using System.Text.Json.Serialization;

namespace BlackSunCyber.ClientAgent;

public class AgentConfig
{
    [JsonPropertyName("StationId")]
    public int StationId { get; set; }

    [JsonPropertyName("ServerUrl")]
    public string ServerUrl { get; set; } = "http://localhost:5000";

    [JsonPropertyName("AccessToken")]
    public string AccessToken { get; set; } = string.Empty;
}