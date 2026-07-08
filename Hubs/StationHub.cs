using BlackSunCyber.Server.Services;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace BlackSunCyber.Server.Hubs
{
    public class StationHub : Hub
    {
        // ==========================================
        // 1. Înregistrări Grupuri (Codul tău existent)
        // ==========================================
        public async Task RegisterStation(int stationId, string accessToken)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, StationService.GroupName(stationId));
        }

        public async Task RegisterAdmin()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
        }

        public async Task RegisterClientWatcher(int stationId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, StationService.GroupName(stationId));
        }

        // ==========================================
        // 2. Funcții Noi: Modul SOS / Asistență
        // ==========================================
        // Când un client apasă pe SOS în Cabinet, trimite alerta la Admins
        public async Task SendSosRequest(int stationId, string user, string type, string message)
        {
            // Trimite instant la toți adminii conectați
            await Clients.Group("admins").SendAsync("OnSosReceived", stationId, user, type, message);
        }

        // Când adminul rezolvă problema și apasă pe ecran
        public async Task ResolveSosRequest(int requestId, int stationId)
        {
            await Clients.Group("admins").SendAsync("OnSosResolved", requestId, stationId);
        }

        // ==========================================
        // 3. Funcții Noi: Modul Bar Virtual
        // ==========================================
        // Când un client comandă ceva de la bar, notifică adminul instant
        public async Task PlaceBarOrder(int stationId, string user, string productName, int quantity, decimal total, string paymentMethod)
        {
            // Trimite comanda în panoul adminului cu sunet/alertă
            await Clients.Group("admins").SendAsync("OnBarOrderReceived", stationId, user, productName, quantity, total, paymentMethod);
        }

        // Când adminul livrează băutura la masă și schimbă statusul
        public async Task UpdateOrderStatus(long orderId, string newStatus)
        {
            await Clients.Group("admins").SendAsync("OnOrderStatusUpdated", orderId, newStatus);
        }
    }
}