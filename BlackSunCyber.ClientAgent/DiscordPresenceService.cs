using DiscordRPC;
using System;

namespace BlackSunCyber.ClientAgent;

/// <summary>
/// Gestionează Discord Rich Presence pentru a arăta statusul clientului
/// direct pe profilul lui de Discord (ex: "Playing at BlackSun Cyber - User: Andrei | PC 05").
/// </summary>
public static class DiscordPresenceService
{
    private static DiscordRpcClient? _client;
    // Client ID implicit al aplicației Discord (poate fi modificat de admin la nevoie)
    private const string DefaultClientId = "1219967675306639450";

    public static void Initialize()
    {
        try
        {
            if (_client != null) return;
            
            _client = new DiscordRpcClient(DefaultClientId);
            _client.Initialize();
        }
        catch (Exception)
        {
            // Eșuează silențios dacă SDK-ul nu poate inițializa (de ex. Discord nu e pornit pe PC)
            _client = null;
        }
    }

    public static void SetPresence(string stationName, string nickname)
    {
        try
        {
            if (_client == null || _client.IsDisposed)
            {
                Initialize();
            }

            if (_client == null) return;

            _client.SetPresence(new RichPresence()
            {
                Details = "Playing at BlackSun Cyber",
                State = $"User: {nickname} | {stationName}",
                Assets = new Assets()
                {
                    LargeImageKey = "blacksun_logo", // Asset încărcat în Discord Dev Portal
                    LargeImageText = "BlackSun Cyber Club",
                    SmallImageKey = "gaming_icon",
                    SmallImageText = "Game On!"
                },
                Timestamps = Timestamps.Now
            });
        }
        catch
        {
            // Ignoră erorile pentru a asigura funcționarea neîntreruptă a restului aplicației
        }
    }

    public static void ClearPresence()
    {
        try
        {
            _client?.ClearPresence();
        }
        catch
        {
            // Eșuează silențios
        }
    }

    public static void Shutdown()
    {
        try
        {
            if (_client != null)
            {
                _client.ClearPresence();
                _client.Dispose();
                _client = null;
            }
        }
        catch
        {
            // Eșuează silențios
        }
    }
}
