using BlackSunCyber.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace BlackSunCyber.Server.Hubs;

/// <summary>
/// Fiecare Client Lock Agent (de pe cele 5 PC-uri) se conectează aici cu
/// access_token-ul lui unic și se alătură grupului propriei stații.
/// Panoul admin se alătură grupului "admins" ca să primească notificări live.
/// </summary>
public class StationHub : Hub
{
    /// <summary>Apelat de agentul WPF la pornire, cu token-ul lui de identificare.</summary>
    public async Task RegisterStation(int stationId, string accessToken)
    {
        // TODO (recomandat): verifică accessToken față de cel din Supabase
        // înainte de a accepta conexiunea, ca să previi conectarea unui
        // agent fals la stația altcuiva.
        await Groups.AddToGroupAsync(Context.ConnectionId, StationService.GroupName(stationId));
    }

    /// <summary>Apelat de interfața admin (browser) la încărcare.</summary>
    public async Task RegisterAdmin()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
    }

    /// <summary>Apelat de portalul client (telefon) ca să primească live update pentru stația lui.</summary>
    public async Task RegisterClientWatcher(int stationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, StationService.GroupName(stationId));
    }
}