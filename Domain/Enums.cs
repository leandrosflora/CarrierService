namespace CarrierService.Domain;

public enum CarrierStatus
{
    Active = 1,
    Degraded = 2,
    Suspended = 3,
    Maintenance = 4,
    Inactive = 5
}

public enum TransportMode
{
    FULFILLMENT = 1,
    FLEX = 2,
    CROSSDOCKING = 3,
    SELLERSHIPPING = 4,
    CARRIER = 5
}

public enum CarrierAvailabilityReason
{
    Available = 1,
    CarrierNotFound = 2,
    CarrierSuspended = 3,
    CarrierDegraded = 4,
    ServiceLevelNotFound = 5,
    LaneNotSupported = 6,
    OutsideOperatingWindow = 7,
    WeightExceeded = 8,
    CubicWeightExceeded = 9,
    FragileItemUnsupported = 10,
    RestrictedItemUnsupported = 11,
    CategoryUnsupported = 12,
    PartnerRejected = 13,
    PartnerUnavailable = 14
}
