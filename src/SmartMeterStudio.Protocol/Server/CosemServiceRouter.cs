using System.Text.Json;
using SmartMeterStudio.Core.Simulation;
using SmartMeterStudio.Protocol.Dlms;

namespace SmartMeterStudio.Protocol.Server;

/// <summary>Maps supported xDLMS LN services to one virtual meter, independent of Blazor and transport I/O.</summary>
public sealed class CosemServiceRouter(SmartMeterFleet fleet, DlmsAssociationContext association)
{
    private readonly SmartMeterFleet _fleet = fleet ?? throw new ArgumentNullException(nameof(fleet));
    private readonly DlmsAssociationContext _association = association ?? throw new ArgumentNullException(nameof(association));

    public DlmsLnResponse Execute(DlmsLnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return request switch
            {
                DlmsGetRequest get => Read(get),
                DlmsSetRequest set => Write(set),
                DlmsActionRequest action => Invoke(action),
                _ => throw new DlmsProtocolException("Unsupported xDLMS request type.")
            };
        }
        catch (KeyNotFoundException) { return Failure(request, DlmsAccessResult.ObjectUndefined); }
        catch (NotSupportedException) { return Failure(request, DlmsAccessResult.ReadWriteDenied); }
        catch (InvalidOperationException) { return Failure(request, DlmsAccessResult.ObjectClassInconsistent); }
        catch (ArgumentException) { return Failure(request, DlmsAccessResult.TypeUnmatched); }
        catch (JsonException) { return Failure(request, DlmsAccessResult.TypeUnmatched); }
    }

    private DlmsGetResponse Read(DlmsGetRequest request)
    {
        if (request.Descriptor.ClassId == 15 && request.Descriptor.LogicalName.ToString() == AssociationLnObject.LogicalName)
            return new(request.InvokeId, DlmsAccessResult.Success, AssociationLnObject.Read(_fleet, _association, request.Descriptor.AttributeId));
        EnsureClass(request.Descriptor.ClassId, request.Descriptor.LogicalName);
        var attribute = _fleet.ReadCosemAttribute(_association.MeterId, request.Descriptor.LogicalName.ToString(), request.Descriptor.AttributeId);
        var value = request.Descriptor.AttributeId == 1 ? DlmsDataValue.Octets(request.Descriptor.LogicalName.ToArray()) : DlmsDataCodec.FromObject(attribute.Value);
        return new(request.InvokeId, DlmsAccessResult.Success, value);
    }
    private DlmsSetResponse Write(DlmsSetRequest request)
    {
        if (!_association.MayWrite) return new(request.InvokeId, DlmsAccessResult.ReadWriteDenied);
        if (request.Descriptor.ClassId == 15 && request.Descriptor.LogicalName.ToString() == AssociationLnObject.LogicalName)
            return new(request.InvokeId, DlmsAccessResult.ReadWriteDenied);
        EnsureClass(request.Descriptor.ClassId, request.Descriptor.LogicalName);
        _fleet.WriteCosem(_association.MeterId, request.Descriptor.LogicalName.ToString(), request.Descriptor.AttributeId, DlmsDataCodec.ToJson(request.Value));
        return new(request.InvokeId, DlmsAccessResult.Success);
    }
    private DlmsActionResponse Invoke(DlmsActionRequest request)
    {
        if (!_association.MayAction) return new(request.InvokeId, DlmsAccessResult.ReadWriteDenied);
        if (request.Descriptor.ClassId == 15 && request.Descriptor.LogicalName.ToString() == AssociationLnObject.LogicalName)
            return new(request.InvokeId, DlmsAccessResult.ReadWriteDenied);
        EnsureClass(request.Descriptor.ClassId, request.Descriptor.LogicalName);
        _fleet.InvokeCosem(_association.MeterId, request.Descriptor.LogicalName.ToString(), request.Descriptor.MethodId,
            request.Parameter is null ? default : DlmsDataCodec.ToJson(request.Parameter));
        return new(request.InvokeId, DlmsAccessResult.Success);
    }
    private void EnsureClass(ushort classId, DlmsLogicalName logicalName)
    {
        if (_fleet.GetCosemObject(_association.MeterId, logicalName.ToString()).ClassId != classId)
            throw new InvalidOperationException("COSEM class does not match the requested object.");
    }
    private static DlmsLnResponse Failure(DlmsLnRequest request, DlmsAccessResult result) => request switch
    {
        DlmsGetRequest => new DlmsGetResponse(request.InvokeId, result),
        DlmsSetRequest => new DlmsSetResponse(request.InvokeId, result),
        DlmsActionRequest => new DlmsActionResponse(request.InvokeId, result),
        _ => throw new DlmsProtocolException("Unsupported xDLMS request type.")
    };
}
