using BlackSunCyber.Server.Models;
using BlackSunCyber.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlackSunCyber.Server.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly StationService _stations;
    private readonly SupabaseRestClient _db;

    public AdminController(StationService stations, SupabaseRestClient db)
    {
        _stations = stations;
        _db = db;
    }

    [HttpGet("stations")]
    public async Task<IActionResult> GetStations() => Ok(await _stations.GetAllStationsAsync());

    [HttpPost("allocate")]
    public async Task<IActionResult> Allocate(AllocateStationRequest req)
    {
        await _stations.AllocateStationAsync(req.StationId, req.Minutes, req.PaymentMethod);
        return Ok();
    }

    [HttpPost("add-time")]
    public async Task<IActionResult> AddTime(AddTimeRequest req)
    {
        await _stations.AddTimeAsync(req.StationId, req.Minutes, req.AmountPaid, req.Note);
        return Ok();
    }

    [HttpPost("force-lock/{stationId:int}")]
    public async Task<IActionResult> ForceLock(int stationId)
    {
        await _stations.ForceLockAsync(stationId);
        return Ok();
    }

    [HttpPost("notifications/{id:long}/resolve")]
    public async Task<IActionResult> ResolveNotification(long id)
    {
        await _stations.ResolveNotificationAsync(id);
        return Ok();
    }

    [HttpGet("price")]
    public async Task<IActionResult> GetPrice() =>
        Ok(new { pricePerHour = await _stations.GetPricePerHourAsync() });

    [HttpGet("server-info")]
    public IActionResult GetServerInfo()
    {
        // Returnează toate IP-urile locale ale serverului
        // Clientul (browser-ul) alege primul IP valid pentru QR code
        var ips = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                     && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .ToList();

        var port = HttpContext.Request.Host.Port ?? 5000;

        return Ok(new
        {
            ips,
            port,
            urls = ips.Select(ip => $"http://{ip}:{port}").ToList()
        });
    }

    [HttpPost("price")]
    public async Task<IActionResult> UpdatePrice([FromBody] UpdatePriceRequest req)
    {
        await _db.PatchAsync<ClubSetting>("club_settings", "key=eq.price_per_hour",
            new { value = req.Price.ToString() });
        return Ok();
    }

    [HttpGet("revenue/today")]
    public async Task<IActionResult> RevenueToday()
    {
        // Luăm tranzacțiile de azi din Supabase
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var transactions = await _db.GetAsync<Transaction>(
            "transactions",
            $"created_at=gte.{today}T00:00:00Z&order=created_at.desc");

        var result = new
        {
            totalAmount = transactions.Sum(t => t.Amount),
            totalMinutes = transactions.Sum(t => t.MinutesAdded),
            totalTransactions = transactions.Count
        };
        return Ok(result);
    }
}

public record UpdatePriceRequest(decimal Price);