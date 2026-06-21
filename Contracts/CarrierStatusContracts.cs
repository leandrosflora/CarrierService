using CarrierService.Domain;

namespace CarrierService.Contracts;

public sealed record CarrierStatusWebhookRequest(
    string EventId,
    string Status,
    DateTimeOffset OccurredAt,
    string? Reason,
    string? Signature);

public sealed record ChangeCarrierStatusRequest(
    CarrierStatus Status,
    string? Reason);

public sealed record CarrierProfileResponse(
    string Code,
    string Name,
    CarrierStatus Status,
    bool RequiresRealTimeValidation);

public sealed record CreateCarrierRequest(
    string Code,
    string Name,
    bool RequiresRealTimeValidation);

public sealed record CreateCarrierServiceLevelRequest(
    string Code,
    string Name,
    TransportMode Mode,
    decimal MaximumWeightKg,
    decimal MaximumCubicWeightKg,
    bool SupportsFragileItems,
    bool SupportsRestrictedItems,
    int Priority);

public sealed record CreateCarrierLaneRequest(
    string ServiceLevelCode,
    Guid OriginNodeId,
    Guid DestinationNodeId,
    string TimeZoneId,
    TimeOnly CutoffTime,
    IReadOnlySet<DayOfWeek> OperatingDays);
