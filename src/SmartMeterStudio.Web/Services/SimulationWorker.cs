using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using SmartMeterStudio.Core.Simulation;

namespace SmartMeterStudio.Web.Services;

public sealed class SimulationWorker(
    SmartMeterFleet fleet,
    IHubContext<MeterHub> hub,
    ILogger<SimulationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Smart-meter simulation engine started with {Count} meters.", fleet.GetSnapshots().Count);
        var stopwatch = Stopwatch.StartNew();
        var previous = stopwatch.Elapsed;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = stopwatch.Elapsed;
            fleet.Tick((current - previous).TotalSeconds);
            previous = current;
            await hub.Clients.All.SendAsync("FleetUpdated", fleet.GetSnapshots(), stoppingToken);
        }
    }
}
