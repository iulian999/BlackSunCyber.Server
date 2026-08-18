using BlackSunCyber.Server.Hubs;
using BlackSunCyber.Server.Models;
using BlackSunCyber.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BlackSunCyber.Server.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly SupabaseRestClient _db;
    private readonly IHubContext<StationHub> _hub;

    public ChatController(SupabaseRestClient db, IHubContext<StationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    // ------------------------------------------------------------------
    // GET /api/chat/{stationId}?limit=50
    // Istoricul mesajelor pentru o stație (cel mai recent primul în response)
    // ------------------------------------------------------------------
    [HttpGet("{stationId:int}")]
    public async Task<IActionResult> GetMessages(int stationId, [FromQuery] int limit = 50)
    {
        var messages = await _db.GetAsync<ChatMessage>(
            "chat_messages",
            $"station_id=eq.{stationId}&order=created_at.asc&limit={limit}");
        return Ok(messages);
    }

    // ------------------------------------------------------------------
    // POST /api/chat/send
    // Trimite un mesaj (client sau admin). Salvează în DB + notifică live.
    // ------------------------------------------------------------------
    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] SendChatMessageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "Mesajul nu poate fi gol." });

        // Salvează în Supabase
        var saved = await _db.PostAsync<ChatMessage>("chat_messages", new
        {
            station_id = req.StationId,
            sender = req.Sender,       // "client" sau "admins"
            sender_name = req.Sender == "admin" ? "Admin" : req.Nickname,
            message = req.Message,
            is_read = false
        });

        var msg = saved.First();

        // Payload compact pentru SignalR
        var payload = new
        {
            id = msg.Id,
            stationId = req.StationId,
            sender = req.Sender,
            senderName = msg.SenderName,
            message = msg.Message,
            createdAt = msg.CreatedAt?.ToString("o")
        };

        // Trimite LIVE la stația respectivă (clientul de pe PC primește mesajul)
        await _hub.Clients.Group(StationService.GroupName(req.StationId))
            .SendAsync("OnChatMessage", payload);

        // Trimite LIVE la toți adminii
        await _hub.Clients.Group("admins")
            .SendAsync("OnChatMessage", payload);

        return Ok(new { messageId = msg.Id });
    }

    // ------------------------------------------------------------------
    // POST /api/chat/{stationId}/mark-read
    // Marchează mesajele ca citite (de admin sau de client)
    // ------------------------------------------------------------------
    [HttpPost("{stationId:int}/mark-read")]
    public async Task<IActionResult> MarkRead(int stationId, [FromBody] MarkReadRequest req)
    {
        // Marchează ca citite mesajele trimise de cealaltă parte
        // (dacă admin citește, marchează mesajele client-ului; vice versa)
        var senderToMark = req.Reader == "admin" ? "client" : "admin";

        await _db.PatchAsync<ChatMessage>(
            "chat_messages",
            $"station_id=eq.{stationId}&sender=eq.{senderToMark}&is_read=eq.false",
            new { is_read = true });

        return Ok();
    }

    // ------------------------------------------------------------------
    // GET /api/chat/unread-counts
    // Returnează numărul de mesaje necitite de la clienți, per stație (pentru admin)
    // ------------------------------------------------------------------
    [HttpGet("unread-counts")]
    public async Task<IActionResult> GetUnreadCounts()
    {
        // Toate mesajele necitite trimise de clienți
        var unread = await _db.GetAsync<ChatMessage>(
            "chat_messages",
            "sender=eq.client&is_read=eq.false&order=station_id.asc");

        // Grupăm per stație
        var counts = unread
            .GroupBy(m => m.StationId)
            .Select(g => new { stationId = g.Key, count = g.Count() })
            .ToList();

        return Ok(counts);
    }
}
