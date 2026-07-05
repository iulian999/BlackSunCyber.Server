using BlackSunCyber.Server.Hubs;
using BlackSunCyber.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace BlackSunCyber.Server.BackgroundWork;

/// <summary>
/// Rulează continuu cât timp serviciul e pornit. La fiecare 10 secunde scade
/// timpul stațiilor 'Active' și, la 0, le blochează automat.
/// </summary>
public class CountdownBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<StationHub> _hub;
    private readonly ILogger<CountdownBackgroundService> _logger;

    public CountdownBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHubContext<StationHub> hub,
        ILogger<CountdownBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Countdown background service pornit.");

        var interval = TimeSpan.FromSeconds(10);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var stationService = scope.ServiceProvider.GetRequiredService<StationService>();

                var stations = await stationService.GetAllStationsAsync();

                foreach (var station in stations.Where(s => s.Status == "Active"))
                {
                    var newRemaining = Math.Max(0, station.RemainingSeconds - (int)interval.TotalSeconds);

                    if (newRemaining == 0)
                    {
                        await stationService.ForceLockAsync(station.Id);
                        _logger.LogInformation("Timp expirat — stația {Id} blocată automat.", station.Id);
                    }
                    else
                    {
                        await stationService.SetRemainingSecondsAsync(station.Id, newRemaining);

                        await _hub.Clients.Group(StationService.GroupName(station.Id))
                            .SendAsync("TimeUpdated", newRemaining, cancellationToken: stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Eroare în countdown background service");
            }
        }
    }
}