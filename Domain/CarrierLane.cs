namespace CarrierService.Domain;

public sealed class CarrierLane
{
    public Guid Id { get; private set; }
    public Guid CarrierServiceLevelId { get; private set; }

    public Guid OriginNodeId { get; private set; }
    public Guid DestinationNodeId { get; private set; }

    public string TimeZoneId { get; private set; } = default!;

    public TimeOnly CutoffTime { get; private set; }

    public bool OperatesOnMonday { get; private set; }
    public bool OperatesOnTuesday { get; private set; }
    public bool OperatesOnWednesday { get; private set; }
    public bool OperatesOnThursday { get; private set; }
    public bool OperatesOnFriday { get; private set; }
    public bool OperatesOnSaturday { get; private set; }
    public bool OperatesOnSunday { get; private set; }

    public bool IsActive { get; private set; }

    private CarrierLane()
    {
    }

    public CarrierLane(
        Guid carrierServiceLevelId,
        Guid originNodeId,
        Guid destinationNodeId,
        string timeZoneId,
        TimeOnly cutoffTime,
        IReadOnlySet<DayOfWeek> operatingDays)
    {
        if (carrierServiceLevelId == Guid.Empty)
            throw new ArgumentException("CarrierServiceLevelId is required", nameof(carrierServiceLevelId));

        if (originNodeId == Guid.Empty || destinationNodeId == Guid.Empty)
            throw new ArgumentException("Origin and destination nodes are required");

        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ArgumentException("Time zone is required", nameof(timeZoneId));

        Id = Guid.NewGuid();
        CarrierServiceLevelId = carrierServiceLevelId;
        OriginNodeId = originNodeId;
        DestinationNodeId = destinationNodeId;
        TimeZoneId = timeZoneId.Trim();
        CutoffTime = cutoffTime;
        OperatesOnMonday = operatingDays.Contains(DayOfWeek.Monday);
        OperatesOnTuesday = operatingDays.Contains(DayOfWeek.Tuesday);
        OperatesOnWednesday = operatingDays.Contains(DayOfWeek.Wednesday);
        OperatesOnThursday = operatingDays.Contains(DayOfWeek.Thursday);
        OperatesOnFriday = operatingDays.Contains(DayOfWeek.Friday);
        OperatesOnSaturday = operatingDays.Contains(DayOfWeek.Saturday);
        OperatesOnSunday = operatingDays.Contains(DayOfWeek.Sunday);
        IsActive = true;
    }

    public bool OperatesAt(DateTimeOffset plannedDepartureUtc)
    {
        if (!IsActive)
            return false;

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var localDeparture = TimeZoneInfo.ConvertTime(plannedDepartureUtc, timeZone);

        var operatesOnDay = localDeparture.DayOfWeek switch
        {
            DayOfWeek.Monday => OperatesOnMonday,
            DayOfWeek.Tuesday => OperatesOnTuesday,
            DayOfWeek.Wednesday => OperatesOnWednesday,
            DayOfWeek.Thursday => OperatesOnThursday,
            DayOfWeek.Friday => OperatesOnFriday,
            DayOfWeek.Saturday => OperatesOnSaturday,
            DayOfWeek.Sunday => OperatesOnSunday,
            _ => false
        };

        return operatesOnDay && TimeOnly.FromDateTime(localDeparture.DateTime) <= CutoffTime;
    }
}
