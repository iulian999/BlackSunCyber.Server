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

        _hub.Reconnected += _ => { OnConnectionStateChanged?.Invoke(true); return Task.CompletedTask; };
        _hub.Reconnecting += _ => { OnConnectionStateChanged?.Invoke(false); return Task.CompletedTask; };
        _hub.Closed += _ => { OnConnectionStateChanged?.Invoke(false); return Task.CompletedTask; };

        await _hub.StartAsync();
        await _hub.InvokeAsync("RegisterStation", _config.StationId, _config.AccessToken);
        OnConnectionStateChanged?.Invoke(true);
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