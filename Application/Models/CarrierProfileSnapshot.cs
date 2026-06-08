using CarrierService.Domain;

namespace CarrierService.Application.Models;

public sealed record CarrierProfileSnapshot(
    Guid CarrierId,
    string Code,
    CarrierStatus Status,
    bool RequiresRealTimeValidation,
    DateTimeOffset StatusUpdatedAt,
    IReadOnlyList<CarrierServiceLevelSnapshot> ServiceLevels);

public sealed record CarrierServiceLevelSnapshot(
    string Code,
    TransportMode Mode,
    decimal MaximumWeightKg,
    decimal MaximumCubicWeightKg,
    bool SupportsFragileItems,
    bool SupportsRestrictedItems,
    int Priority,
    bool IsActive,
    IReadOnlySet<string> BlockedCategories,
    IReadOnlyList<CarrierLaneSnapshot> Lanes);

public sealed record CarrierLaneSnapshot(
    Guid OriginNodeId,
    Guid DestinationNodeId,
    string TimeZoneId,
    TimeOnly CutoffTime,
    IReadOnlySet<DayOfWeek> OperatingDays,
    bool IsActive);
