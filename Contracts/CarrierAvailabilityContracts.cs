namespace CarrierService.Contracts;

public sealed record SearchCarrierAvailabilityRequest(
    IReadOnlyList<CarrierAvailabilityCheckRequest> Checks);

public sealed record CarrierAvailabilityCheckRequest(
    string CheckId,
    string CarrierCode,
    string? ServiceLevelCode,
    Guid OriginNodeId,
    Guid DestinationNodeId,
    string DestinationPostalCode,
    DateTimeOffset PlannedDepartureAtUtc,
    PackageProfileDto Package);

public sealed record PackageProfileDto(
    decimal WeightKg,
    decimal CubicWeightKg,
    bool IsFragile,
    bool IsRestricted,
    string? Category);

public sealed record SearchCarrierAvailabilityResponse(
    IReadOnlyList<CarrierAvailabilityResult> Results);

public sealed record CarrierAvailabilityResult(
    string CheckId,
    string CarrierCode,
    string? ServiceLevelCode,
    bool Available,
    string ReasonCode,
    string Source,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset? ValidUntil);
