using BlackSunCyber.Server.Hubs;
using BlackSunCyber.Server.Models;
using BlackSunCyber.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BlackSunCyber.Server.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingController : ControllerBase
{
    private readonly SupabaseRestClient _db;
    private readonly StationService _stations;
    private readonly IHubContext<StationHub> _hub;

    public BookingController(SupabaseRestClient db, StationService stations, IHubContext<StationHub> hub)
    {
        _db = db;
        _stations = stations;
        _hub = hub;
    }

    // ------------------------------------------------------------------
    // GET /api/bookings/stations-live
    // Status live al tuturor stațiilor — folosit de pagina publică de rezervare
    // ------------------------------------------------------------------
    [HttpGet("stations-live")]
    public async Task<IActionResult> GetStationsLive()
    {
        var stations = await _stations.GetAllStationsAsync();
        // Returnăm doar informațiile relevante (nu expunem token-ul de acces)
        var result = stations.Select(s => new
        {
            id = s.Id,
            name = s.StationName,
            status = s.Status,   // Locked = disponibil, Active/Pending/Paused = ocupat
            isAvailable = s.Status == "Locked" || s.Status == "Offline" == false && s.Status == "Locked"
        });
        return Ok(result);
    }

    // ------------------------------------------------------------------
    // GET /api/bookings?status=pending
    // Lista rezervărilor pentru admin
    // ------------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> GetBookings([FromQuery] string? status = null)
    {
        var query = status != null
            ? $"status=eq.{status}&order=scheduled_at.asc"
            : "order=scheduled_at.asc";

        var bookings = await _db.GetAsync<Booking>("bookings", query);
        return Ok(bookings);
    }

    // ------------------------------------------------------------------
    // POST /api/bookings
    // Creează o rezervare nouă (din pagina publică de rezervare)
    // ------------------------------------------------------------------
    [HttpPost]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Nickname))
            return BadRequest(new { error = "Nickname-ul este obligatoriu." });

        if (req.ScheduledAt < DateTime.UtcNow.AddMinutes(-5))
            return BadRequest(new { error = "Nu poți rezerva în trecut." });

        var created = await _db.PostAsync<Booking>("bookings", new
        {
            station_id = req.StationId,
            nickname = req.Nickname,
            phone = req.Phone,
            scheduled_at = req.ScheduledAt.ToString("o"),
            duration_minutes = req.DurationMinutes,
            status = "pending",
            note = req.Note
        });

        var booking = created.First();

        // Notifică adminul live
        await _hub.Clients.Group("admins").SendAsync(
            "NewBooking",
            booking.Id,
            booking.StationId,
            booking.Nickname,
            booking.Phone,
            booking.ScheduledAt.ToString("yyyy-MM-dd HH:mm"),
            booking.DurationMinutes);

        return Ok(new { bookingId = booking.Id, status = booking.Status });
    }

    // ------------------------------------------------------------------
    // POST /api/bookings/{id}/confirm
    // ------------------------------------------------------------------
    [HttpPost("{id:long}/confirm")]
    public async Task<IActionResult> Confirm(long id)
    {
        await _db.PatchAsync<Booking>("bookings", $"id=eq.{id}", new { status = "confirmed" });
        return Ok();
    }

    // ------------------------------------------------------------------
    // POST /api/bookings/{id}/cancel
    // ------------------------------------------------------------------
    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        await _db.PatchAsync<Booking>("bookings", $"id=eq.{id}", new { status = "cancelled" });
        return Ok();
    }

    // ------------------------------------------------------------------
    // POST /api/bookings/{id}/complete
    // ------------------------------------------------------------------
    [HttpPost("{id:long}/complete")]
    public async Task<IActionResult> Complete(long id)
    {
        await _db.PatchAsync<Booking>("bookings", $"id=eq.{id}", new { status = "completed" });
        return Ok();
    }

    // ------------------------------------------------------------------
    // GET /api/bookings/upcoming
    // Rezervările din următoarele 24h (pentru reminder admin)
    // ------------------------------------------------------------------
    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming()
    {
        var from = DateTime.UtcNow.ToString("o");
        var to = DateTime.UtcNow.AddHours(24).ToString("o");
        var bookings = await _db.GetAsync<Booking>(
            "bookings",
            $"scheduled_at=gte.{from}&scheduled_at=lte.{to}&status=in.(pending,confirmed)&order=scheduled_at.asc");
        return Ok(bookings);
    }
}
