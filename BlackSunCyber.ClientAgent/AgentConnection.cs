using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace BlackSunCyber.ClientAgent;

/// <summary>
/// Toată comunicarea cu server-ul trece prin clasa asta: conexiunea SignalR
/// (pentru comenzi instant: ShowNicknamePrompt, Unlock, TimeUpdated, ForceLock)
/// și apelurile HTTP (pentru acțiuni inițiate de client: trimite nickname,
/// cere prelungire, heartbeat).
/// </summary>
public class AgentConnection
{
    private readonly AgentConfig _config;
    private readonly HttpClient _http;
    private HubConnection? _hub;
    public string ServerUrl => _config.ServerUrl;


    public event Action<int>? OnShowNicknamePrompt;       // minute alocate
    public event Action<string, int>? OnUnlock;            // nickname, remainingSeconds
    public event Action<int>? OnTimeUpdated;                // remainingSeconds
    public event Action? OnForceLock;
    public event Action<bool>? OnConnectionStateChanged;    // true = conectat

    public AgentConnection(AgentConfig config)
    {
        _config = config;
        _http = new HttpClient { BaseAddress = new Uri(config.ServerUrl) };
    }

    public async Task ConnectAsync()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl($"{_config.ServerUrl}/hub/station", options =>
            {
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .WithAutomaticReconnect()
            .Build();

        _hub.On<int>("ShowNicknamePrompt", minutes => OnShowNicknamePrompt?.Invoke(minutes));
        _hub.On<string, int>("Unlock", (nickname, seconds) => OnUnlock?.Invoke(nickname, seconds));
        _hub.On<int>("TimeUpdated", seconds => OnTimeUpdated?.Invoke(seconds));
        _hub.On("ForceLock", () => OnForceLock?.Invoke());

        _hub.Reconnected += async _ =>
        {
            // CRITICAL: La reconectare, Connection ID se schimbă și grupul se pierde.
            // Trebuie să ne re-înregistrăm în grupul stației, altfel nu mai primim
            // comenzi de la server (Unlock, ForceLock, TimeUpdated etc.)
            try
            {
                await _hub.InvokeAsync("RegisterStation", _config.StationId, _config.AccessToken);

                // Sincronizăm starea curentă de pe server (poate s-a schimbat cât eram deconectați)
                await SyncStateFromServerAsync();
            }
            catch { /* va reîncerca la următorul reconnect */ }
            OnConnectionStateChanged?.Invoke(true);
        };
        _hub.Reconnecting += _ => { OnConnectionStateChanged?.Invoke(false); return Task.CompletedTask; };
        _hub.Closed += _ => { OnConnectionStateChanged?.Invoke(false); return Task.CompletedTask; };

        await _hub.StartAsync();
        await _hub.InvokeAsync("RegisterStation", _config.StationId, _config.AccessToken);
        OnConnectionStateChanged?.Invoke(true);

        // Sincronizăm starea la prima conectare (în caz că serverul a alocat timp cât clientul era oprit)
        await SyncStateFromServerAsync();
    }

    /// <summary>
    /// Cere starea curentă de la server și invocă evenimentele corecte
    /// (ShowNicknamePrompt / Unlock / ForceLock) pentru a sincroniza UI-ul.
    /// </summary>
    private async Task SyncStateFromServerAsync()
    {
        try
        {
            var response = await _http.GetAsync($"api/client/station/{_config.StationId}");
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var status = root.GetProperty("status").GetString() ?? "Locked";
            var remainingSeconds = root.TryGetProperty("remaining_seconds", out var rs) ? rs.GetInt32() : 0;
            var nickname = root.TryGetProperty("current_user_name", out var un) ? un.GetString() : null;

            switch (status)
            {
                case "Pending":
                    var minutes = remainingSeconds / 60;
                    OnShowNicknamePrompt?.Invoke(minutes > 0 ? minutes : 1);
                    break;
                case "Active" when !string.IsNullOrEmpty(nickname):
                    OnUnlock?.Invoke(nickname, remainingSeconds);
                    break;
                default:
                    OnForceLock?.Invoke();
                    break;
            }
        }
        catch { /* non-critical — dacă serverul nu e accesibil, rămânem în starea curentă */ }
    }

    public async Task SendNicknameAsync(string nickname)
    {
        await _http.PostAsJsonAsync("api/client/set-nickname", new
        {
            stationId = _config.StationId,
            nickname
        });
    }

    public async Task RequestExtensionAsync(string nickname, int requestedMinutes)
    {
        await _http.PostAsJsonAsync("api/client/request-extension", new
        {
            stationId = _config.StationId,
            nickname,
            requestedMinutes
        });
    }

    public async Task SendHeartbeatAsync()
    {
        try { await _http.PostAsync($"api/client/heartbeat/{_config.StationId}", null); }
        catch { /* dacă serverul e momentan inaccesibil, încercăm din nou la următorul tick */ }
    }

    public async Task<decimal> GetPricePerHourAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<PriceResponse>("api/client/price");
            return result?.PricePerHour ?? 20m;
        }
        catch { return 20m; }
    }

    private record PriceResponse(decimal PricePerHour);
}