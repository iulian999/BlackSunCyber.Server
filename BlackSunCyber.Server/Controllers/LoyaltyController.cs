using BlackSunCyber.Server.Hubs;
using BlackSunCyber.Server.Models;
using BlackSunCyber.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BlackSunCyber.Server.Controllers;

[ApiController]
[Route("api/loyalty")]
public class LoyaltyController : ControllerBase
{
    private readonly LoyaltyService _loyalty;
    private readonly IHubContext<StationHub> _hub;

    public LoyaltyController(LoyaltyService loyalty, IHubContext<StationHub> hub)
    {
        _loyalty = loyalty;
        _hub = hub;
    }

    // GET /api/loyalty/profile/{nickname}
    [HttpGet("profile/{nickname}")]
    public async Task<IActionResult> GetProfile(string nickname)
    {
        var profile = await _loyalty.GetOrCreateProfileAsync(nickname);
        return Ok(profile);
    }

    // GET /api/loyalty/leaderboard?type=coins&limit=15
    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] string type = "coins",
        [FromQuery] int limit = 15)
    {
        var list = type == "minutes"
            ? await _loyalty.GetLeaderboardByMinutesAsync(limit)
            : await _loyalty.GetLeaderboardAsync(limit);

        return Ok(list);
    }

    // POST /api/loyalty/add  (admin: acordă coins manual)
    [HttpPost("add")]
    public async Task<IActionResult> AddCoins([FromBody] SpendCoinsRequest req)
    {
        await _loyalty.AddCoinsAsync(req.Nickname, req.Amount, req.Reason);
        var profile = await _loyalty.GetProfileAsync(req.Nickname);
        return Ok(new { success = true, newBalance = profile?.SunCoins ?? 0 });
    }

    // POST /api/loyalty/spend  (client: cheltuiește coins)
    [HttpPost("spend")]
    public async Task<IActionResult> SpendCoins([FromBody] SpendCoinsRequest req)
    {
        var (success, newBalance, error) = await _loyalty.SpendCoinsAsync(req.Nickname, req.Amount, req.Reason);
        if (!success)
            return BadRequest(new { error });

        // Notifică clientul despre noul sold (în timp real)
        try
        {
            var profile = await _loyalty.GetProfileAsync(req.Nickname);
            if (profile != null)
            {
                // Trimite actualizare la toți adminii (știu că clientul a cheltuit coins)
                await _hub.Clients.Group("admins").SendAsync(
                    "CoinsSpent", req.Nickname, req.Amount, req.Reason, newBalance);
            }
        }
        catch { /* non-critical */ }

        return Ok(new { success = true, newBalance });
    }
}
