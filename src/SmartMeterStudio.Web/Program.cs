using System.Text.Json.Serialization;
using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Persistence;
using SmartMeterStudio.Core.Simulation;
using SmartMeterStudio.Web.Components;
using SmartMeterStudio.Web.Infrastructure;
using SmartMeterStudio.Web.Services;
using SmartMeterStudio.Protocol.Monitoring;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IMeterDefinitionStore, JsonMeterDefinitionStore>();
builder.Services.AddSingleton<SmartMeterFleet>();
builder.Services.AddSingleton<ProtocolFrameMonitor>();
builder.Services.AddSingleton<IProtocolFrameMonitor>(services => services.GetRequiredService<ProtocolFrameMonitor>());
builder.Services.AddSingleton<SerialDlmsGateway>();
builder.Services.AddHostedService<SimulationWorker>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePages();
app.UseAntiforgery();
app.MapStaticAssets();

var api = app.MapGroup("/api");
api.MapGet("/health", (SmartMeterFleet fleet) => Results.Ok(new
{
    status = "healthy",
    virtualMeters = fleet.GetSnapshots().Count,
    timestamp = DateTimeOffset.UtcNow
}));
api.MapGet("/meters", (SmartMeterFleet fleet) => Results.Ok(fleet.GetSnapshots()));
api.MapGet("/meters/{id}", (string id, SmartMeterFleet fleet) =>
    fleet.GetSnapshot(id) is { } meter ? Results.Ok(meter) : Results.NotFound());
api.MapPost("/meters", (CreateMeterRequest request, SmartMeterFleet fleet) =>
{
    var meter = fleet.Create(request);
    return Results.Created($"/api/meters/{meter.Definition.Id}", meter);
});
api.MapDelete("/meters/{id}", (string id, SmartMeterFleet fleet) =>
    fleet.Remove(id) ? Results.NoContent() : Results.NotFound());
api.MapPost("/meters/{id}/start", (string id, SmartMeterFleet fleet) =>
    fleet.Start(id) ? Results.Ok(fleet.GetSnapshot(id)) : Results.NotFound());
api.MapPost("/meters/{id}/stop", (string id, SmartMeterFleet fleet) =>
    fleet.Stop(id) ? Results.Ok(fleet.GetSnapshot(id)) : Results.NotFound());
api.MapPost("/meters/{id}/time-scale", (string id, TimeScaleRequest request, SmartMeterFleet fleet) =>
    fleet.SetTimeScale(id, request.Scale) ? Results.Ok(fleet.GetSnapshot(id)) : Results.NotFound());
api.MapPost("/meters/{id}/faults", (string id, FaultRequest request, SmartMeterFleet fleet) =>
    fleet.InjectFault(id, request.Type, request.DurationSeconds) ? Results.Ok(fleet.GetSnapshot(id)) : Results.NotFound());
api.MapDelete("/meters/{id}/faults", (string id, SmartMeterFleet fleet) =>
    fleet.ClearFault(id) ? Results.Ok(fleet.GetSnapshot(id)) : Results.NotFound());
api.MapGet("/meters/{id}/readings", (string id, int? limit, SmartMeterFleet fleet) =>
    fleet.GetReadings(id, limit ?? 120) is { } readings ? Results.Ok(readings) : Results.NotFound());
api.MapGet("/meters/{id}/events", (string id, int? limit, SmartMeterFleet fleet) =>
    fleet.GetEvents(id, limit ?? 50) is { } events ? Results.Ok(events) : Results.NotFound());
api.MapGet("/protocol/frames", (string? meterId, int? limit, IProtocolFrameMonitor monitor) =>
    Results.Ok(monitor.Recent(meterId, limit ?? 200)));
api.MapGet("/protocol/serial", (SerialDlmsGateway gateway) => Results.Ok(gateway.Status));

app.MapGet("/sim/{id}/api/v1/read-instant", (string id, SmartMeterFleet fleet) =>
{
    var snapshot = fleet.GetSnapshot(id);
    if (snapshot is null) return Results.NotFound();
    if (!snapshot.IsReachable) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    var reading = snapshot.LatestReading;
    return reading is null ? Results.NoContent() : Results.Ok(new
    {
        meterId = snapshot.Definition.SerialNumber,
        deviceName = snapshot.Definition.Name,
        datetime = reading.Timestamp,
        voltage = new { r = reading.VoltageL1, y = reading.VoltageL2, b = reading.VoltageL3 },
        current = new { r = reading.CurrentL1, y = reading.CurrentL2, b = reading.CurrentL3 },
        frequency = reading.FrequencyHz,
        activePowerKw = reading.ActivePowerKw,
        reactivePowerKvar = reading.ReactivePowerKvar,
        powerFactor = reading.PowerFactor,
        importEnergyKwh = reading.ImportEnergyKwh,
        exportEnergyKwh = reading.ExportEnergyKwh,
        maximumDemandKw = reading.MaximumDemandKw,
        voltageThdPercent = reading.VoltageThdPercent,
        tariffRate = reading.TariffRate
    });
});
app.MapGet("/sim/{id}/api/v1/read-bill", (string id, SmartMeterFleet fleet) =>
{
    var snapshot = fleet.GetSnapshot(id);
    if (snapshot is null) return Results.NotFound();
    if (!snapshot.IsReachable) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    var reading = snapshot.LatestReading;
    return Results.Ok(new
    {
        meterId = snapshot.Definition.SerialNumber,
        billingDate = snapshot.SimulatedTime,
        importEnergyKwh = reading?.ImportEnergyKwh ?? 0,
        exportEnergyKwh = reading?.ExportEnergyKwh ?? 0,
        maximumDemandKw = reading?.MaximumDemandKw ?? 0,
        tariffPlan = snapshot.Definition.TariffPlan
    });
});
app.MapGet("/sim/{id}/api/v1/read-tamper", (string id, SmartMeterFleet fleet) =>
    fleet.GetEvents(id, 100) is { } events
        ? Results.Ok(events.Where(item => item.Code.Contains("FAULT", StringComparison.OrdinalIgnoreCase)))
        : Results.NotFound());
app.MapGet("/sim/{id}/api/v1/status-datacollection", (string id, SmartMeterFleet fleet) =>
    fleet.GetSnapshot(id) is { } meter
        ? Results.Ok(new { meterId = meter.Definition.SerialNumber, connected = meter.IsReachable, running = meter.State == MeterOperatingState.Running })
        : Results.NotFound());

app.MapHub<MeterHub>("/hubs/meters");
app.MapCompanionApi();
app.MapCosemApi();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
