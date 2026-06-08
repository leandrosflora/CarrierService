using CarrierService.Contracts;

namespace CarrierService.Adapters;

public interface ICarrierAdapter
{
    string CarrierCode { get; }

    Task<IReadOnlyList<ExternalCarrierAvailabilityResult>> CheckAvailabilityAsync(
        IReadOnlyList<ExternalCarrierAvailabilityRequest> checks,
        CancellationToken cancellationToken);

    Task<CarrierHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}

public sealed record ExternalCarrierAvailabilityRequest(
    string CheckId,
    string ServiceLevelCode,
    Guid OriginNodeId,
    Guid DestinationNodeId,
    string DestinationPostalCode,
    DateTimeOffset PlannedDepartureAtUtc,
    PackageProfileDto Package);

public sealed record ExternalCarrierAvailabilityResult(
    string CheckId,
    bool Available,
    string? RejectionCode,
    DateTimeOffset? ValidUntil);

public sealed record CarrierHealthResult(bool Healthy, string? Reason);
