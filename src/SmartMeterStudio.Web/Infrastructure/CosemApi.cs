using System.Text.Json;
using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Simulation;

namespace SmartMeterStudio.Web.Infrastructure;

public static class CosemApi
{
    public static void MapCosemApi(this WebApplication app)
    {
        app.MapGet("/api/cosem/classes", () => CosemCatalog.Classes);
        var group = app.MapGroup("/api/meters/{id}/cosem");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (KeyNotFoundException error) { return Results.NotFound(new { error = error.Message }); }
            catch (NotSupportedException error) { return Results.Json(new { error = error.Message }, statusCode: 422); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        });
        group.MapGet("/objects", (string id, SmartMeterFleet fleet) => fleet.GetCosemObjects(id));
        group.MapGet("/objects/{ln}", (string id, string ln, SmartMeterFleet fleet) => fleet.GetCosemObject(id, ln));
        group.MapPost("/objects", (string id, CreateCosemObject request, SmartMeterFleet fleet) =>
        { fleet.CreateCosem(id, request); return Results.Created($"/api/meters/{id}/cosem/objects/{request.LogicalName}", fleet.GetCosemObject(id, request.LogicalName)); });
        group.MapDelete("/objects/{ln}", (string id, string ln, SmartMeterFleet fleet) =>
        { fleet.DeleteCosem(id, ln); return Results.NoContent(); });
        group.MapGet("/objects/{ln}/attributes/{index:int}", (string id, string ln, int index, SmartMeterFleet fleet) =>
            fleet.ReadCosemAttribute(id, ln, index));
        group.MapPut("/objects/{ln}/attributes/{index:int}", (string id, string ln, int index, ValueChange request, SmartMeterFleet fleet) =>
        { fleet.WriteCosem(id, ln, index, request.Value); return Results.NoContent(); });
        group.MapPost("/objects/{ln}/methods/{index:int}", (string id, string ln, int index, ActionRequest request, SmartMeterFleet fleet) =>
        { fleet.InvokeCosem(id, ln, index, request.Parameter); return Results.NoContent(); });
        group.MapGet("/profiles/{ln}/configuration", (string id, string ln, SmartMeterFleet fleet) => fleet.GetCosemProfileConfiguration(id, ln));
        group.MapPut("/profiles/{ln}/configuration", (string id, string ln, ProfileConfiguration request, SmartMeterFleet fleet) =>
        { fleet.ConfigureCosemProfile(id, ln, request); return Results.NoContent(); });
        group.MapGet("/profiles/{ln}/rows", (string id, string ln, DateTimeOffset? from, DateTimeOffset? to, int? start, int? count, SmartMeterFleet fleet) =>
            fleet.GetCosemProfileRows(id, ln, from, to, start ?? 1, count ?? 100));
    }
    public sealed record ValueChange(JsonElement Value);
    public sealed record ActionRequest(JsonElement Parameter = default);
}
