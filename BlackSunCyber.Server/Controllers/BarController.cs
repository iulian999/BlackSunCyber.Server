using BlackSunCyber.Server.Hubs;
using BlackSunCyber.Server.Models;
using BlackSunCyber.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BlackSunCyber.Server.Controllers;

[ApiController]
[Route("api/bar")]
public class BarController : ControllerBase
{
    private readonly SupabaseRestClient _db;
    private readonly IHubContext<StationHub> _hub;

    public BarController(SupabaseRestClient db, IHubContext<StationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    // ---- PRODUSE ----

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts()
    {
        var products = await _db.GetAsync<BarProduct>("bar_products", "is_available=eq.true&order=category.asc,name.asc");
        return Ok(products);
    }

    [HttpGet("products/all")]
    public async Task<IActionResult> GetAllProducts()
    {
        var products = await _db.GetAsync<BarProduct>("bar_products", "order=category.asc,name.asc");
        return Ok(products);
    }

    [HttpPost("products")]
    public async Task<IActionResult> AddProduct([FromBody] BarProduct product)
    {
        var result = await _db.PostAsync<BarProduct>("bar_products", new
        {
            name = product.Name,
            category = product.Category,
            price = product.Price,
            emoji = product.Emoji,
            image_url = product.ImageUrl,
            stock = product.Stock,
            is_available = product.IsAvailable
        });
        return Ok(result.FirstOrDefault());
    }

    [HttpPatch("products/{id:int}")]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] BarProduct product)
    {
        await _db.PatchAsync<BarProduct>("bar_products", $"id=eq.{id}", new
        {
            name = product.Name,
            category = product.Category,
            price = product.Price,
            emoji = product.Emoji,
            image_url = product.ImageUrl,
            stock = product.Stock,
            is_available = product.IsAvailable
        });
        return Ok();
    }

    [HttpDelete("products/{id:int}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        // Stergere reala din baza de date
        await _db.DeleteAsync("bar_products", $"id=eq.{id}");
        return Ok();
    }

    [HttpPost("order")]
    public async Task<IActionResult> PlaceOrder(BarOrderRequest req)
    {
        var products = await _db.GetAsync<BarProduct>("bar_products", $"id=eq.{req.ProductId}&is_available=eq.true");
        var product = products.FirstOrDefault();
        if (product == null) return BadRequest(new { error = "Produsul nu este disponibil." });

        var order = await _db.PostAsync<BarOrder>("bar_orders", new
        {
            station_id = req.StationId,
            current_user_name = req.Nickname,
            product_id = req.ProductId,
            product_name = product.Name,
            product_price = product.Price,
            quantity = req.Quantity,
            status = "pending"
        });

        var created = order.First();

        // Notificare live la admin
        await _hub.Clients.Group("admins").SendAsync("NewBarOrder",
            created.Id, req.StationId, req.Nickname,
            $"{req.Quantity}x {product.Emoji} {product.Name} ({product.Price * req.Quantity} MDL)");

        return Ok(new { orderId = created.Id });
    }

    [HttpGet("orders/pending")]
    public async Task<IActionResult> GetPendingOrders()
    {
        var orders = await _db.GetAsync<BarOrder>("bar_orders", "status=eq.pending&order=created_at.asc");
        return Ok(orders);
    }

    [HttpPost("orders/{id:long}/deliver")]
    public async Task<IActionResult> DeliverOrder(long id)
    {
        await _db.PatchAsync<BarOrder>("bar_orders", $"id=eq.{id}", new { status = "delivered" });
        return Ok();
    }

    [HttpPost("orders/{id:long}/cancel")]
    public async Task<IActionResult> CancelOrder(long id)
    {
        await _db.PatchAsync<BarOrder>("bar_orders", $"id=eq.{id}", new { status = "cancelled" });
        return Ok();
    }

    // ---- SOS / ASISTENTA ----

    [HttpPost("sos")]
    public async Task<IActionResult> SendSos(SosRequest req)
    {
        var result = await _db.PostAsync<SosRequestModel>("sos_requests", new
        {
            station_id = req.StationId,
            current_user_name = req.Nickname,
            type = req.Type,
            message = req.Message,
            is_resolved = false
        });

        var created = result.First();

        // Notificare urgenta la admin
        var emoji = req.Type switch
        {
            "audio" => "🎧",
            "cleaning" => "🧹",
            "noise" => "⚠️",
            _ => "🆘"
        };
        await _hub.Clients.Group("admins").SendAsync("NewSosRequest",
            created.Id, req.StationId, req.Nickname,
            $"{emoji} PC {req.StationId}: {req.Message}");

        return Ok(new { sosId = created.Id });
    }

    [HttpGet("sos/pending")]
    public async Task<IActionResult> GetPendingSos()
    {
        var requests = await _db.GetAsync<SosRequestModel>("sos_requests", "is_resolved=eq.false&order=created_at.asc");
        return Ok(requests);
    }

    [HttpPost("sos/{id:long}/resolve")]
    public async Task<IActionResult> ResolveSos(long id)
    {
        await _db.PatchAsync<SosRequestModel>("sos_requests", $"id=eq.{id}", new { is_resolved = true });
        return Ok();
    }
}