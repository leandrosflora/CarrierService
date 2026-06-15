using CarrierService.Domain;

namespace CarrierService.UnitTests.Domain;

public sealed class CarrierDomainTests
{
    [Fact]
    public void Constructor_NormalizesCodeAndNameAndStartsActive()
    {
        var carrier = new Carrier(" meli-logistics ", " Meli Logistics ", requiresRealTimeValidation: true);

        Assert.Equal("MELI-LOGISTICS", carrier.Code);
        Assert.Equal("Meli Logistics", carrier.Name);
        Assert.Equal(CarrierStatus.Active, carrier.Status);
        Assert.True(carrier.RequiresRealTimeValidation);
    }

    [Fact]
    public void AddServiceLevel_WhenValuesAreValid_NormalizesCodeAndAddsActiveServiceLevel()
    {
        var carrier = new Carrier("MELI", "Meli", false);

        var serviceLevel = carrier.AddServiceLevel(" standard ", " Standard ", TransportMode.Road, 10m, 20m, true, false, 1);

        Assert.Equal("STANDARD", serviceLevel.Code);
        Assert.Equal("Standard", serviceLevel.Name);
        Assert.True(serviceLevel.IsActive);
        Assert.Contains(serviceLevel, carrier.ServiceLevels);
    }

    [Fact]
    public void CarrierLane_OperatesAt_RespectsOperatingDaysAndCutoffInConfiguredTimezone()
    {
        var lane = new CarrierLane(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "UTC", new TimeOnly(18, 0), new HashSet<DayOfWeek> { DayOfWeek.Monday });

        Assert.True(lane.OperatesAt(new DateTimeOffset(2026, 6, 15, 17, 59, 0, TimeSpan.Zero)));
        Assert.False(lane.OperatesAt(new DateTimeOffset(2026, 6, 15, 18, 1, 0, TimeSpan.Zero)));
        Assert.False(lane.OperatesAt(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void CarrierServiceLevel_SupportsPackage_RejectsUnsupportedPackageProfiles()
    {
        var serviceLevel = new CarrierServiceLevel(Guid.NewGuid(), "STD", "Standard", TransportMode.Road, 10m, 20m, supportsFragileItems: false, supportsRestrictedItems: false, priority: 1);

        Assert.True(serviceLevel.SupportsPackage(10m, 20m, isFragile: false, isRestricted: false));
        Assert.False(serviceLevel.SupportsPackage(10.1m, 20m, isFragile: false, isRestricted: false));
        Assert.False(serviceLevel.SupportsPackage(10m, 20.1m, isFragile: false, isRestricted: false));
        Assert.False(serviceLevel.SupportsPackage(10m, 20m, isFragile: true, isRestricted: false));
        Assert.False(serviceLevel.SupportsPackage(10m, 20m, isFragile: false, isRestricted: true));
    }
}
