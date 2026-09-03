using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Simulation;

namespace SmartMeterStudio.Web.Infrastructure;

public static class CompanionApi
{
    public static void MapCompanionApi(this WebApplication app)
    {
        app.MapGet("/api/standards/coverage", () => Results.Ok(CompanionCoverage.Requirements));
        // Local simulator management API. These endpoints are not DLMS associations.
        var group = app.MapGroup("/api/meters/{id}/companion");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        });
        group.MapGet("/", (string id, SmartMeterFleet fleet) => fleet.GetCompanion(id) is { } value ? Results.Ok(value) : Results.NotFound());
        group.MapGet("/objects", (string id, SmartMeterFleet fleet) => fleet.GetObjects(id) is { } values ? Results.Ok(values) : Results.NotFound());
        group.MapGet("/electrical", (string id, SmartMeterFleet fleet) => fleet.GetElectrical(id) is { } value ? Results.Ok(value) : Results.NotFound());
        group.MapPut("/electrical", (string id, ElectricalSettings settings, SmartMeterFleet fleet) => Result(fleet.ConfigureElectrical(id, settings)));
        group.MapGet("/profiles/{kind}", (string id, SurveyKind kind, DateTimeOffset? from, DateTimeOffset? to, SmartMeterFleet fleet) =>
            fleet.GetSurvey(id, kind, from, to) is { } values ? Results.Ok(values) : Results.NotFound());
        group.MapPut("/settings", (string id, CompanionSettings settings, SmartMeterFleet fleet) => Result(fleet.ConfigureCompanion(id, settings)));
        group.MapPut("/relay", (string id, RelayChange change, SmartMeterFleet fleet) => Result(fleet.SetRelay(id, change.Connected)));
        group.MapPut("/clock", (string id, ClockChange change, SmartMeterFleet fleet) => Result(fleet.SetClock(id, change.Time)));
        group.MapPost("/billing/close", (string id, SmartMeterFleet fleet) => Result(fleet.CloseBilling(id)));
        group.MapPut("/prepayment", (string id, PrepaymentState state, SmartMeterFleet fleet) => Result(fleet.SetPrepayment(id, state)));
        group.MapPost("/messages", (string id, MessageChange change, SmartMeterFleet fleet) => Result(fleet.SendMessage(id, change.FromUtility, change.Message)));
        group.MapPost("/push-preview", (string id, SmartMeterFleet fleet) => Result(fleet.PreviewPush(id)));
        group.MapPost("/firmware/initiate", (string id, FirmwareStart change, SmartMeterFleet fleet) => Result(fleet.BeginFirmware(id, change.ImageId, change.Size, change.Sha256)));
        group.MapPost("/firmware/block", (string id, FirmwareBlock change, SmartMeterFleet fleet) => Result(fleet.WriteFirmwareBlock(id, change.Offset, change.Bytes)));
        group.MapPost("/firmware/verify", (string id, SmartMeterFleet fleet) => Result(fleet.VerifyFirmware(id)));
        group.MapPost("/firmware/activate", (string id, SmartMeterFleet fleet) => Result(fleet.ActivateFirmware(id)));
    }
    private static IResult Result(bool found) => found ? Results.NoContent() : Results.NotFound();
    public sealed record RelayChange(bool Connected);
    public sealed record ClockChange(DateTimeOffset Time);
    public sealed record MessageChange(bool FromUtility, string Message);
    public sealed record FirmwareStart(string ImageId, int Size, string Sha256);
    public sealed record FirmwareBlock(int Offset, byte[] Bytes);
}
