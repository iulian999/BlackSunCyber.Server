using System.Text.Json.Serialization;

namespace BlackSunCyber.Server.Models;

public class Station
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("station_name")]
    public string StationName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Locked"; // Locked | Pending | Active | Paused | Offline

    [JsonPropertyName("remaining_seconds")]
    public int RemainingSeconds { get; set; }

    [JsonPropertyName("current_user_name")]
    public string? CurrentUserName { get; set; }

    [JsonPropertyName("session_pin")]
    public int? SessionPin { get; set; }

    [JsonPropertyName("access_token")]
    public Guid AccessToken { get; set; }

    [JsonPropertyName("last_heartbeat")]
    public DateTime? LastHeartbeat { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

public class Transaction
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("station_id")]
    public int StationId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("minutes_added")]
    public int MinutesAdded { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

public class Notification
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("station_id")]
    public int StationId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("is_resolved")]
    public bool IsResolved { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

public class ClubSetting
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

// ---- DTO-uri folosite în API (nu mapează direct pe tabele) ----

public record AllocateStationRequest(int StationId, int Minutes);

public record SetNicknameRequest(int StationId, string Nickname);

public record AddTimeRequest(int StationId, int Minutes, decimal AmountPaid, string? Note);

public record ClientLoginRequest(string Nickname, int? Pin);

public record RequestExtensionRequest(int StationId, string Nickname, int RequestedMinutes);