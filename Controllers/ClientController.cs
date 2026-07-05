using BlackSunCyber.Server.Models;
using BlackSunCyber.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlackSunCyber.Server.Controllers;

[ApiController]
[Route("api/client")]
public class ClientController : ControllerBase
{
    private readonly StationService _stations;

    public ClientController(StationService stations) => _stations = stations;

    /// <summary>
    /// Pas 2 din flux: clientul introduce nickname-ul pe ecranul de la stație
    /// (sau, dacă vrei să permiți și de pe telefon, același endpoint funcționează).
    /// </summary>
    [HttpPost("set-nickname")]
    public async Task<IActionResult> SetNickname(SetNicknameRequest req)
    {
        var ok = await _stations.SetNicknameAsync(req.StationId, req.Nickname);
        if (!ok) return BadRequest(new { error = "Stația nu e în starea 'Pending' sau nu există." });
        return Ok();
    }

    /// <summary>
    /// Login pe portalul de telefon: clientul introduce nickname-ul (+ opțional PIN)
    /// și primește datele stației lui, ca să le poată afișa live.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(ClientLoginRequest req)
    {
        var station = await _stations.FindByNicknameAsync(req.Nickname);
        if (station == null) return NotFound(new { error = "Nu există nicio sesiune activă cu acest nickname." });

        if (req.Pin.HasValue && station.SessionPin.HasValue && station.SessionPin != req.Pin)
            return Unauthorized(new { error = "PIN incorect." });

        return Ok(station);
    }

    /// <summary>Cerere de prelungire timp, trimisă fie din PC, fie din telefon.</summary>
    [HttpPost("request-extension")]
    public async Task<IActionResult> RequestExtension(RequestExtensionRequest req)
    {
        var id = await _stations.RequestExtensionAsync(req.StationId, req.Nickname, req.RequestedMinutes);
        return Ok(new { notificationId = id });
    }

    [HttpGet("station/{stationId:int}")]
    public async Task<IActionResult> GetStation(int stationId)
    {
        var station = await _stations.GetStationAsync(stationId);
        if (station == null) return NotFound();
        return Ok(station);
    }

    [HttpPost("heartbeat/{stationId:int}")]
    public async Task<IActionResult> Heartbeat(int stationId)
    {
        await _stations.RecordHeartbeatAsync(stationId);
        return Ok();
    }

    [HttpGet("price")]
    public async Task<IActionResult> GetPrice() => Ok(new { pricePerHour = await _stations.GetPricePerHourAsync() });
}