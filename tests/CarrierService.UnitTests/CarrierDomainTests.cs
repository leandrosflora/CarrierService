using CarrierService.Domain;
using Xunit;

namespace CarrierService.UnitTests;

public class CarrierTests
{
    [Fact]
    public void Constructor_NormalizesCodeToUpperInvariant()
    {
        var carrier = new Carrier("meli", "Meli Logistics", false);

        Assert.Equal("MELI", carrier.Code);
    }

    [Fact]
    public void Constructor_TrimsCodeAndName()
    {
        var carrier = new Carrier("  meli  ", "  Meli Logistics  ", false);

        Assert.Equal("MELI", carrier.Code);
        Assert.Equal("Meli Logistics", carrier.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsWhenCodeIsEmpty(string code)
    {
        Assert.Throws<ArgumentException>(() => new Carrier(code, "Name", false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsWhenNameIsEmpty(string name)
    {
        Assert.Throws<ArgumentException>(() => new Carrier("CODE", name, false));
    }

    [Fact]
    public void Constructor_SetsInitialStatusToActive()
    {
        var carrier = new Carrier("CODE", "Name", false);

        Assert.Equal(CarrierStatus.Active, carrier.Status);
    }

    [Fact]
    public void Constructor_AssignsNonEmptyId()
    {
        var carrier = new Carrier("CODE", "Name", false);

        Assert.NotEqual(Guid.Empty, carrier.Id);
    }

    [Fact]
    public void Constructor_SetsRequiresRealTimeValidation()
    {
        var carrier = new Carrier("CODE", "Name", requiresRealTimeValidation: true);

        Assert.True(carrier.RequiresRealTimeValidation);
    }

    [Fact]
    public void ChangeStatus_UpdatesStatus()
    {
        var carrier = new Carrier("CODE", "Name", false);

        carrier.ChangeStatus(CarrierStatus.Suspended);

        Assert.Equal(CarrierStatus.Suspended, carrier.Status);
    }

    [Fact]
    public void ChangeStatus_UpdatesStatusUpdatedAt()
    {
        var carrier = new Carrier("CODE", "Name", false);
        var before = DateTimeOffset.UtcNow;

        carrier.ChangeStatus(CarrierStatus.Degraded);

        Assert.True(carrier.StatusUpdatedAt >= before);
    }

    [Fact]
    public void ChangeStatus_UpdatesUpdatedAt()
    {
        var carrier = new Carrier("CODE", "Name", false);
        var before = DateTimeOffset.UtcNow;

        carrier.ChangeStatus(CarrierStatus.Maintenance);

        Assert.True(carrier.UpdatedAt >= before);
    }

    [Fact]
    public void AddServiceLevel_AddsToServiceLevels()
    {
        var carrier = new Carrier("CODE", "Name", false);

        carrier.AddServiceLevel("EXPRESS", "Express", TransportMode.CARRIER, 30m, 30m, true, true, 1);

        Assert.Single(carrier.ServiceLevels);
        Assert.Equal("EXPRESS", carrier.ServiceLevels[0].Code);
    }

    [Fact]
    public void AddServiceLevel_ReturnsCreatedServiceLevel()
    {
        var carrier = new Carrier("CODE", "Name", false);

        var sl = carrier.AddServiceLevel("ECONOMY", "Economy", TransportMode.CARRIER, 10m, 10m, false, false, 2);

        Assert.NotNull(sl);
        Assert.Equal("ECONOMY", sl.Code);
        Assert.Equal(carrier.Id, sl.CarrierId);
    }

    [Fact]
    public void AddServiceLevel_MultipleServiceLevels_AllAdded()
    {
        var carrier = new Carrier("CODE", "Name", false);

        carrier.AddServiceLevel("EXPRESS", "Express", TransportMode.CARRIER, 30m, 30m, true, true, 1);
        carrier.AddServiceLevel("ECONOMY", "Economy", TransportMode.CARRIER, 10m, 10m, false, false, 2);

        Assert.Equal(2, carrier.ServiceLevels.Count);
    }
}

public class CarrierServiceLevelTests
{
    private static readonly Guid CarrierId = Guid.NewGuid();

    [Fact]
    public void Constructor_NormalizesCodeToUpperInvariant()
    {
        var sl = new CarrierServiceLevel(CarrierId, "express", "Express", TransportMode.CARRIER, 30m, 30m, true, true, 1);

        Assert.Equal("EXPRESS", sl.Code);
    }

    [Fact]
    public void Constructor_ThrowsWhenCarrierIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new CarrierServiceLevel(Guid.Empty, "CODE", "Name", TransportMode.CARRIER, 30m, 30m, true, true, 1));
    }

    [Fact]
    public void Constructor_ThrowsWhenCodeIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new CarrierServiceLevel(CarrierId, "", "Name", TransportMode.CARRIER, 30m, 30m, true, true, 1));
    }

    [Fact]
    public void Constructor_ThrowsWhenMaxWeightIsZeroOrNegative()
    {
        Assert.Throws<ArgumentException>(() =>
            new CarrierServiceLevel(CarrierId, "CODE", "Name", TransportMode.CARRIER, 0m, 30m, true, true, 1));
    }

    [Fact]
    public void Constructor_ThrowsWhenMaxCubicWeightIsNegative()
    {
        Assert.Throws<ArgumentException>(() =>
            new CarrierServiceLevel(CarrierId, "CODE", "Name", TransportMode.CARRIER, 30m, -1m, true, true, 1));
    }

    [Fact]
    public void Constructor_SetsIsActiveToTrue()
    {
        var sl = new CarrierServiceLevel(CarrierId, "CODE", "Name", TransportMode.CARRIER, 30m, 30m, true, true, 1);

        Assert.True(sl.IsActive);
    }

    [Fact]
    public void SupportsPackage_WhenWeightExceeded_ReturnsFalse()
    {
        var sl = new CarrierServiceLevel(CarrierId, "CODE", "Name", TransportMode.CARRIER, 5m, 30m, true, true, 1);

        Assert.False(sl.SupportsPackage(10m, 1m, false, false));
    }

    [Fact]
    public void SupportsPackage_WhenCubicWeightExceeded_ReturnsFalse()
    {
        var sl = new CarrierServiceLevel(CarrierId, "CODE", "Name", TransportMode.CARRIER, 30m, 5m, true, true, 1);

        Assert.False(sl.SupportsPackage(1m, 10m, false, false));
    }

    [Fact]
    public void SupportsPackage_WhenFragileAndNotSupported_ReturnsFalse()
    {
        var sl = new CarrierServiceLevel(CarrierId, "CODE", "Name", TransportMode.CARRIER, 30m, 30m, false, true, 1);

        Assert.False(sl.SupportsPackage(1m, 1m, true, false));
    }

    [Fact]
    public void SupportsPackage_WhenRestrictedAndNotSupported_ReturnsFalse()
    {
        var sl = new CarrierServiceLevel(CarrierId, "CODE", "Name", TransportMode.CARRIER, 30m, 30m, true, false, 1);

        Assert.False(sl.SupportsPackage(1m, 1m, false, true));
    }

    [Fact]
    public void SupportsPackage_WhenAllConditionsMet_ReturnsTrue()
    {
        var sl = new CarrierServiceLevel(CarrierId, "CODE", "Name", TransportMode.CARRIER, 30m, 30m, true, true, 1);

        Assert.True(sl.SupportsPackage(1m, 1m, false, false));
    }
}

public class CarrierLaneTests
{
    private static readonly Guid ServiceLevelId = Guid.NewGuid();
    private static readonly Guid OriginId = Guid.NewGuid();
    private static readonly Guid DestId = Guid.NewGuid();

    // Monday 2026-06-29 10:00 UTC
    private static readonly DateTimeOffset MondayAt10 = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ThrowsWhenServiceLevelIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new CarrierLane(Guid.Empty, OriginId, DestId, "UTC", new TimeOnly(23, 59),
                new HashSet<DayOfWeek> { DayOfWeek.Monday }));
    }

    [Fact]
    public void Constructor_ThrowsWhenOriginIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new CarrierLane(ServiceLevelId, Guid.Empty, DestId, "UTC", new TimeOnly(23, 59),
                new HashSet<DayOfWeek> { DayOfWeek.Monday }));
    }

    [Fact]
    public void Constructor_ThrowsWhenTimeZoneIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new CarrierLane(ServiceLevelId, OriginId, DestId, "", new TimeOnly(23, 59),
                new HashSet<DayOfWeek> { DayOfWeek.Monday }));
    }

    [Fact]
    public void Constructor_SetsIsActiveToTrue()
    {
        var lane = BuildLane();

        Assert.True(lane.IsActive);
    }

    [Fact]
    public void Constructor_SetsOperatingDaysFromSet()
    {
        var lane = new CarrierLane(ServiceLevelId, OriginId, DestId, "UTC", new TimeOnly(23, 59),
            new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday });

        Assert.True(lane.OperatesOnMonday);
        Assert.False(lane.OperatesOnTuesday);
        Assert.True(lane.OperatesOnWednesday);
        Assert.False(lane.OperatesOnThursday);
    }

    [Fact]
    public void OperatesAt_WhenOnOperatingDayBeforeCutoff_ReturnsTrue()
    {
        var lane = BuildLane(
            operatingDays: new HashSet<DayOfWeek> { DayOfWeek.Monday },
            cutoffTime: new TimeOnly(12, 0));

        Assert.True(lane.OperatesAt(MondayAt10));
    }

    [Fact]
    public void OperatesAt_WhenOnNonOperatingDay_ReturnsFalse()
    {
        var lane = BuildLane(operatingDays: new HashSet<DayOfWeek> { DayOfWeek.Tuesday });

        // MondayAt10 is Monday
        Assert.False(lane.OperatesAt(MondayAt10));
    }

    [Fact]
    public void OperatesAt_WhenAfterCutoffTime_ReturnsFalse()
    {
        var afterCutoff = new DateTimeOffset(2026, 6, 29, 15, 0, 0, TimeSpan.Zero);
        var lane = BuildLane(
            operatingDays: new HashSet<DayOfWeek> { DayOfWeek.Monday },
            cutoffTime: new TimeOnly(12, 0));

        Assert.False(lane.OperatesAt(afterCutoff));
    }

    [Fact]
    public void OperatesAt_WhenExactlyAtCutoffTime_ReturnsTrue()
    {
        var atCutoff = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        var lane = BuildLane(
            operatingDays: new HashSet<DayOfWeek> { DayOfWeek.Monday },
            cutoffTime: new TimeOnly(12, 0));

        Assert.True(lane.OperatesAt(atCutoff));
    }

    private static CarrierLane BuildLane(
        IReadOnlySet<DayOfWeek>? operatingDays = null,
        TimeOnly? cutoffTime = null)
    {
        return new CarrierLane(
            ServiceLevelId, OriginId, DestId, "UTC",
            cutoffTime ?? new TimeOnly(23, 59),
            operatingDays ?? new HashSet<DayOfWeek>
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday
            });
    }
}
