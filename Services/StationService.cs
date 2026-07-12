using BlackSunCyber.Server.Hubs;
using BlackSunCyber.Server.Models;
using Microsoft.AspNetCore.SignalR;

namespace BlackSunCyber.Server.Services;

/// <summary>
/// Toată logica de business trece prin acest serviciu: el e singurul punct
/// care scrie în Supabase (prin SupabaseRestClient, cu Secret key, deci
/// ocolește RLS) și care trimite comenzi în timp real către agenții de pe
/// stații prin SignalR.
/// </summary>
public class StationService
{
    private readonly SupabaseRestClient _db;
    private readonly IHubContext<StationHub> _hub;
    private readonly ILogger<StationService> _logger;

    public StationService(SupabaseRestClient db, IHubContext<StationHub> hub, ILogger<StationService> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    // ------------------------------------------------------------
    // CITIRE
    // ------------------------------------------------------------

    public Task<List<Station>> GetAllStationsAsync() =>
        _db.GetAsync<Station>("stations", "order=id.asc");

    public async Task<Station?> GetStationAsync(int id)
    {
        var result = await _db.GetAsync<Station>("stations", $"id=eq.{id}");
        return result.FirstOrDefault();
    }

    /// <summary>Caută o stație după nickname-ul curent — folosit de login-ul de pe telefon.</summary>
    public async Task<Station?> FindByNicknameAsync(string nickname)
    {
        var result = await _db.GetAsync<Station>("stations", $"current_user_name=eq.{Uri.EscapeDataString(nickname)}");
        return result.FirstOrDefault();
    }

    public async Task<decimal> GetPricePerHourAsync()
    {
        var result = await _db.GetAsync<ClubSetting>("club_settings", "key=eq.price_per_hour");
        var setting = result.FirstOrDefault();
        return setting != null && decimal.TryParse(setting.Value, out var price) ? price : 20m;
    }

    // ------------------------------------------------------------
    // PAS 1: Admin alocă o stație + timp (fără nickname încă)
    // Stația trece în 'Pending' — agentul afișează ecranul de bun venit
    // ------------------------------------------------------------
    public async Task AllocateStationAsync(int stationId, int minutes, string? paymentMethod)
    {
        var pin = new Random().Next(1000, 9999);

        await _db.PatchAsync<Station>("stations", $"id=eq.{stationId}", new
        {
            status = "Pending",
            remaining_seconds = minutes * 60,
            session_pin = pin,
            current_user_name = (string?)null
        });

        // Înregistrăm tranzacția o singură dată aici
        var setting = (await _db.GetAsync<ClubSetting>("club_settings", "key=eq.price_per_hour")).FirstOrDefault();
        var pricePerHour = setting != null && decimal.TryParse(setting.Value, out var p) ? p : 20m;
        var amount = (paymentMethod == "CARD_PENDING") ? 0m : Math.Round(minutes / 60m * pricePerHour, 2);

        await _db.PostAsync<Transaction>("transactions", new
        {
            station_id = stationId,
            amount,
            minutes_added = minutes,
            note = $"Alocare initiala | Metoda: {paymentMethod ?? "CASH"}"
        });

        _logger.LogInformation("Statia {Id} alocata: {Min} min, PIN {Pin}", stationId, minutes, pin);
        await _hub.Clients.Group(GroupName(stationId)).SendAsync("ShowNicknamePrompt", minutes);
    }

    // ------------------------------------------------------------
    // PAS 2: Clientul introduce nickname-ul pe PC. Stația trece în 'Active'.
    // ------------------------------------------------------------
    public async Task<bool> SetNicknameAsync(int stationId, string nickname)
    {
        var station = await GetStationAsync(stationId);
        if (station == null || station.Status != "Pending") return false;

        await _db.PatchAsync<Station>("stations", $"id=eq.{stationId}", new
        {
            current_user_name = nickname,
            status = "Active"
        });

        _logger.LogInformation("Stația {Id} activată pentru {Nick}", stationId, nickname);

        // Notifica agentul de pe statie sa se deblocheze
        await _hub.Clients.Group(GroupName(stationId)).SendAsync("Unlock", nickname, station.RemainingSeconds);

        // Notifica si panoul admin sa actualizeze grila instant (fara refresh manual)
        await _hub.Clients.Group("admins").SendAsync("StationUpdated", stationId, "Active", nickname, station.RemainingSeconds);

        return true;
    }

    // ------------------------------------------------------------
    // Adăugare timp
    // ------------------------------------------------------------
    public async Task AddTimeAsync(int stationId, int minutes, decimal amountPaid, string? note)
    {
        var station = await GetStationAsync(stationId);
        if (station == null) return;

        var newSeconds = station.RemainingSeconds + minutes * 60;

        await _db.PatchAsync<Station>("stations", $"id=eq.{stationId}", new { remaining_seconds = newSeconds });

        await _db.PostAsync<Transaction>("transactions", new
        {
            station_id = stationId,
            amount = amountPaid,
            minutes_added = minutes,
            note
        });

        await _hub.Clients.Group(GroupName(stationId)).SendAsync("TimeUpdated", newSeconds);
        _logger.LogInformation("Adăugat {Min} min ({Amount} MDL) la stația {Id}", minutes, amountPaid, stationId);
    }

    // ------------------------------------------------------------
    // Client cere prelungire -> notificare la admin
    // ------------------------------------------------------------
    public async Task<long> RequestExtensionAsync(int stationId, string nickname, int requestedMinutes, string? paymentMethod)
    {
        var paymentLabel = paymentMethod == "CARD_PENDING" ? "💳 CARD ONLINE (coming soon)" : "💵 CASH";
        var created = await _db.PostAsync<Notification>("notifications", new
        {
            station_id = stationId,
            message = $"{nickname} cere prelungire cu {requestedMinutes} minute [{paymentLabel}].",
            is_resolved = false
        });

        var notification = created.First();
        await _hub.Clients.Group("admins").SendAsync("NewNotification", notification.Id, stationId, notification.Message);

        return notification.Id;
    }

    public Task ResolveNotificationAsync(long notificationId) =>
        _db.PatchAsync<Notification>("notifications", $"id=eq.{notificationId}", new { is_resolved = true });

    // ------------------------------------------------------------
    // Blocare forțată / terminare sesiune
    // ------------------------------------------------------------
    public async Task ForceLockAsync(int stationId)
    {
        await _db.PatchAsync<Station>("stations", $"id=eq.{stationId}", new
        {
            status = "Locked",
            remaining_seconds = 0,
            current_user_name = (string?)null,
            session_pin = (int?)null
        });

        await _hub.Clients.Group(GroupName(stationId)).SendAsync("ForceLock");
        _logger.LogInformation("Stația {Id} blocată forțat de admin", stationId);
    }

    // ------------------------------------------------------------
    // Heartbeat
    // ------------------------------------------------------------
    public Task RecordHeartbeatAsync(int stationId) =>
        _db.PatchAsync<Station>("stations", $"id=eq.{stationId}", new { last_heartbeat = DateTime.UtcNow });

    /// <summary>Doar pentru background service, ca să poată scrie direct remaining_seconds.</summary>
    public Task SetRemainingSecondsAsync(int stationId, int seconds) =>
        _db.PatchAsync<Station>("stations", $"id=eq.{stationId}", new { remaining_seconds = seconds });

    public static string GroupName(int stationId) => $"station-{stationId}";
}