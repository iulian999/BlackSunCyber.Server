using BlackSunCyber.Server.Models;

namespace BlackSunCyber.Server.Services;

/// <summary>
/// Gestionează economia virtuală SunCoins: profiluri jucători, acordare/cheltuire monede,
/// statistici sesiuni și leaderboard local.
/// Toate operațiile sunt additive — nu modifică logica existentă de stații.
/// </summary>
public class LoyaltyService
{
    private readonly SupabaseRestClient _db;
    private readonly ILogger<LoyaltyService> _logger;

    // 1 SunCoin la fiecare X minute jucate
    private const int CoinsPerMinutes = 10;

    public LoyaltyService(SupabaseRestClient db, ILogger<LoyaltyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Profil
    // ------------------------------------------------------------------

    /// <summary>
    /// Returnează profilul jucătorului sau îl creează dacă nu există.
    /// </summary>
    public async Task<PlayerProfile> GetOrCreateProfileAsync(string nickname)
    {
        try
        {
            var existing = await _db.GetAsync<PlayerProfile>(
                "player_profiles", $"nickname=eq.{Uri.EscapeDataString(nickname)}");

            if (existing.Count > 0)
                return existing[0];

            // Crează profil nou
            var created = await _db.PostAsync<PlayerProfile>("player_profiles", new
            {
                nickname,
                sun_coins = 0,
                total_minutes_played = 0,
                total_sessions = 0
            });

            _logger.LogInformation("Profil nou creat pentru '{Nickname}'", nickname);
            return created.First();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la GetOrCreateProfile pentru '{Nickname}'", nickname);
            // Returnează un profil gol pentru a nu bloca fluxul existent
            return new PlayerProfile { Nickname = nickname };
        }
    }

    /// <summary>Returnează profilul sau null dacă nu există.</summary>
    public async Task<PlayerProfile?> GetProfileAsync(string nickname)
    {
        try
        {
            var result = await _db.GetAsync<PlayerProfile>(
                "player_profiles", $"nickname=eq.{Uri.EscapeDataString(nickname)}");
            return result.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la GetProfile pentru '{Nickname}'", nickname);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Acordare monede
    // ------------------------------------------------------------------

    /// <summary>
    /// Acordă SunCoins după încheierea unei sesiuni.
    /// Se apelează din StationService.ForceLockAsync după calculul minutelor.
    /// </summary>
    public async Task AwardSessionCoinsAsync(string nickname, int minutesPlayed)
    {
        if (string.IsNullOrWhiteSpace(nickname) || minutesPlayed <= 0) return;

        try
        {
            var coinsEarned = minutesPlayed / CoinsPerMinutes;
            if (coinsEarned <= 0) coinsEarned = 1; // minim 1 coin per sesiune completă

            var profile = await GetOrCreateProfileAsync(nickname);

            var newCoins = profile.SunCoins + coinsEarned;
            var newMinutes = profile.TotalMinutesPlayed + minutesPlayed;
            var newSessions = profile.TotalSessions + 1;

            await _db.PatchAsync<PlayerProfile>(
                "player_profiles",
                $"nickname=eq.{Uri.EscapeDataString(nickname)}",
                new
                {
                    sun_coins = newCoins,
                    total_minutes_played = newMinutes,
                    total_sessions = newSessions,
                    updated_at = DateTime.UtcNow
                });

            await _db.PostAsync<SunCoinTransaction>("sun_coin_transactions", new
            {
                nickname,
                amount = coinsEarned,
                reason = $"Sesiune {minutesPlayed} minute"
            });

            _logger.LogInformation(
                "'{Nickname}' a primit {Coins} SunCoins pentru {Min} minute (total: {Total})",
                nickname, coinsEarned, minutesPlayed, newCoins);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la AwardSessionCoins pentru '{Nickname}'", nickname);
        }
    }

    /// <summary>Adaugă un număr arbitrar de coins (bonus admin, promoție etc.).</summary>
    public async Task AddCoinsAsync(string nickname, int amount, string reason)
    {
        if (string.IsNullOrWhiteSpace(nickname) || amount <= 0) return;

        try
        {
            var profile = await GetOrCreateProfileAsync(nickname);
            var newCoins = profile.SunCoins + amount;

            await _db.PatchAsync<PlayerProfile>(
                "player_profiles",
                $"nickname=eq.{Uri.EscapeDataString(nickname)}",
                new { sun_coins = newCoins, updated_at = DateTime.UtcNow });

            await _db.PostAsync<SunCoinTransaction>("sun_coin_transactions", new
            {
                nickname,
                amount,
                reason
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la AddCoins pentru '{Nickname}'", nickname);
        }
    }

    // ------------------------------------------------------------------
    // Cheltuire monede
    // ------------------------------------------------------------------

    /// <summary>
    /// Cheltuiește SunCoins. Returnează (success, newBalance, errorMessage).
    /// </summary>
    public async Task<(bool Success, int NewBalance, string Error)> SpendCoinsAsync(
        string nickname, int amount, string reason)
    {
        if (amount <= 0)
            return (false, 0, "Suma trebuie să fie pozitivă.");

        try
        {
            var profile = await GetOrCreateProfileAsync(nickname);
            if (profile.SunCoins < amount)
                return (false, profile.SunCoins, $"Sold insuficient: {profile.SunCoins} SunCoins disponibili.");

            var newCoins = profile.SunCoins - amount;

            await _db.PatchAsync<PlayerProfile>(
                "player_profiles",
                $"nickname=eq.{Uri.EscapeDataString(nickname)}",
                new { sun_coins = newCoins, updated_at = DateTime.UtcNow });

            await _db.PostAsync<SunCoinTransaction>("sun_coin_transactions", new
            {
                nickname,
                amount = -amount, // negativ = cheltuire
                reason
            });

            _logger.LogInformation(
                "'{Nickname}' a cheltuit {Amount} SunCoins pentru '{Reason}' (ramas: {New})",
                nickname, amount, reason, newCoins);

            return (true, newCoins, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la SpendCoins pentru '{Nickname}'", nickname);
            return (false, 0, "Eroare internă.");
        }
    }

    // ------------------------------------------------------------------
    // Leaderboard
    // ------------------------------------------------------------------

    /// <summary>Top jucători după SunCoins (all-time).</summary>
    public async Task<List<PlayerProfile>> GetLeaderboardAsync(int limit = 15)
    {
        try
        {
            return await _db.GetAsync<PlayerProfile>(
                "player_profiles",
                $"order=sun_coins.desc&limit={limit}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la GetLeaderboard");
            return new List<PlayerProfile>();
        }
    }

    /// <summary>Top jucători după minute jucate (all-time).</summary>
    public async Task<List<PlayerProfile>> GetLeaderboardByMinutesAsync(int limit = 15)
    {
        try
        {
            return await _db.GetAsync<PlayerProfile>(
                "player_profiles",
                $"order=total_minutes_played.desc&limit={limit}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la GetLeaderboardByMinutes");
            return new List<PlayerProfile>();
        }
    }
}
