using CarrierService.Application;
using CarrierService.Application.Models;
using CarrierService.Contracts;
using CarrierService.Domain;
using Xunit;

namespace CarrierService.UnitTests;

public class CarrierStaticRuleEvaluatorTests
{
    private readonly CarrierStaticRuleEvaluator _sut = new();

    private static readonly Guid OriginId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DestId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // Monday 2026-06-29 10:00 UTC
    private static readonly DateTimeOffset MondayAt10 = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CarrierStatus.Suspended)]
    [InlineData(CarrierStatus.Maintenance)]
    [InlineData(CarrierStatus.Inactive)]
    public void Evaluate_WhenCarrierUnavailableStatus_ReturnsCarrierSuspended(CarrierStatus status)
    {
        var profile = BuildProfile(status: status);

        var result = _sut.Evaluate(profile, BuildCheck());

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.CarrierSuspended, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenCarrierDegraded_ReturnsCarrierDegraded()
    {
        var profile = BuildProfile(status: CarrierStatus.Degraded);

        var result = _sut.Evaluate(profile, BuildCheck());

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.CarrierDegraded, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenNoActiveServiceLevels_ReturnsServiceLevelNotFound()
    {
        var profile = BuildProfile(serviceLevelIsActive: false);

        var result = _sut.Evaluate(profile, BuildCheck());

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.ServiceLevelNotFound, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenRequestedServiceLevelCodeNotFound_ReturnsServiceLevelNotFound()
    {
        var profile = BuildProfile();

        var result = _sut.Evaluate(profile, BuildCheck(serviceLevelCode: "NONEXISTENT"));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.ServiceLevelNotFound, result.Reason);
    }

    [Fact]
    public void Evaluate_ServiceLevelCodeMatchIsCaseInsensitive()
    {
        var profile = BuildProfile();

        var result = _sut.Evaluate(profile, BuildCheck(serviceLevelCode: "express"));

        Assert.True(result.Available);
    }

    [Fact]
    public void Evaluate_WhenWeightExceeded_ReturnsWeightExceeded()
    {
        var profile = BuildProfile(maxWeightKg: 5m);

        var result = _sut.Evaluate(profile, BuildCheck(package: BuildPackage(weightKg: 10m)));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.WeightExceeded, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenCubicWeightExceeded_ReturnsCubicWeightExceeded()
    {
        var profile = BuildProfile(maxCubicWeightKg: 5m);

        var result = _sut.Evaluate(profile, BuildCheck(package: BuildPackage(cubicWeightKg: 10m)));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.CubicWeightExceeded, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenFragileItemAndNotSupported_ReturnsFragileItemUnsupported()
    {
        var profile = BuildProfile(supportsFragile: false);

        var result = _sut.Evaluate(profile, BuildCheck(package: BuildPackage(isFragile: true)));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.FragileItemUnsupported, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenFragileItemAndSupported_DoesNotReturnFragileReason()
    {
        var profile = BuildProfile(supportsFragile: true);

        var result = _sut.Evaluate(profile, BuildCheck(package: BuildPackage(isFragile: true)));

        Assert.True(result.Available);
    }

    [Fact]
    public void Evaluate_WhenRestrictedItemAndNotSupported_ReturnsRestrictedItemUnsupported()
    {
        var profile = BuildProfile(supportsRestricted: false);

        var result = _sut.Evaluate(profile, BuildCheck(package: BuildPackage(isRestricted: true)));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.RestrictedItemUnsupported, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenCategoryBlocked_ReturnsCategoryUnsupported()
    {
        var profile = BuildProfile(blockedCategories: new HashSet<string> { "ELECTRONICS" });

        // blocked category comparison is case-insensitive
        var result = _sut.Evaluate(profile, BuildCheck(package: BuildPackage(category: "electronics")));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.CategoryUnsupported, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenCategoryNotBlocked_ReturnsAvailable()
    {
        var profile = BuildProfile(blockedCategories: new HashSet<string> { "ELECTRONICS" });

        var result = _sut.Evaluate(profile, BuildCheck(package: BuildPackage(category: "CLOTHING")));

        Assert.True(result.Available);
    }

    [Fact]
    public void Evaluate_WhenLaneNotFound_ReturnsLaneNotSupported()
    {
        var profile = BuildProfile();

        var result = _sut.Evaluate(profile, BuildCheck(originNodeId: Guid.NewGuid()));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.LaneNotSupported, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenInactiveLane_ReturnsOutsideOperatingWindow()
    {
        // DetermineFailure finds the lane by node IDs without filtering IsActive,
        // so it treats the inactive lane as "exists but outside operating window".
        var inactiveLane = BuildLane(isActive: false);
        var sl = BuildServiceLevel(lanes: [inactiveLane]);
        var profile = BuildProfile(serviceLevels: [sl]);

        var result = _sut.Evaluate(profile, BuildCheck());

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.OutsideOperatingWindow, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenDepartureOnNonOperatingDay_ReturnsOutsideOperatingWindow()
    {
        // Saturday 2026-07-04
        var saturday = new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero);
        var profile = BuildProfile(operatingDays: new HashSet<DayOfWeek> { DayOfWeek.Monday });

        var result = _sut.Evaluate(profile, BuildCheck(departure: saturday));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.OutsideOperatingWindow, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenDepartureAfterCutoffTime_ReturnsOutsideOperatingWindow()
    {
        // Monday 15:00 UTC, cutoff 12:00 UTC
        var afterCutoff = new DateTimeOffset(2026, 6, 29, 15, 0, 0, TimeSpan.Zero);
        var profile = BuildProfile(
            cutoffTime: new TimeOnly(12, 0),
            operatingDays: new HashSet<DayOfWeek> { DayOfWeek.Monday });

        var result = _sut.Evaluate(profile, BuildCheck(departure: afterCutoff));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.OutsideOperatingWindow, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenDepartureExactlyAtCutoffTime_ReturnsAvailable()
    {
        // cutoff is 10:00 and departure is also 10:00
        var atCutoff = new DateTimeOffset(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        var profile = BuildProfile(
            cutoffTime: new TimeOnly(10, 0),
            operatingDays: new HashSet<DayOfWeek> { DayOfWeek.Monday });

        var result = _sut.Evaluate(profile, BuildCheck(departure: atCutoff));

        Assert.True(result.Available);
    }

    [Fact]
    public void Evaluate_WhenAllConditionsMet_ReturnsAvailable()
    {
        var profile = BuildProfile();

        var result = _sut.Evaluate(profile, BuildCheck());

        Assert.True(result.Available);
        Assert.Equal(CarrierAvailabilityReason.Available, result.Reason);
        Assert.Equal("EXPRESS", result.ServiceLevelCode);
    }

    [Fact]
    public void Evaluate_WhenAvailableWithRealTimeRequired_PropagatesFlag()
    {
        var profile = BuildProfile(requiresRealTime: true);

        var result = _sut.Evaluate(profile, BuildCheck());

        Assert.True(result.Available);
        Assert.True(result.RequiresRealTimeValidation);
    }

    [Fact]
    public void Evaluate_WhenAvailableWithoutRealTime_FlagIsFalse()
    {
        var profile = BuildProfile(requiresRealTime: false);

        var result = _sut.Evaluate(profile, BuildCheck());

        Assert.True(result.Available);
        Assert.False(result.RequiresRealTimeValidation);
    }

    [Fact]
    public void Evaluate_SkipsFailingServiceLevelAndPicksNextByPriority()
    {
        var lane = BuildLane();
        var failing = BuildServiceLevel("ECONOMY", priority: 1, maxWeightKg: 1m, lanes: [lane]);
        var passing = BuildServiceLevel("EXPRESS", priority: 2, maxWeightKg: 30m, lanes: [lane]);
        var profile = BuildProfile(serviceLevels: [failing, passing]);

        var result = _sut.Evaluate(profile, BuildCheck(package: BuildPackage(weightKg: 5m)));

        Assert.True(result.Available);
        Assert.Equal("EXPRESS", result.ServiceLevelCode);
    }

    [Fact]
    public void Evaluate_ReturnsFirstPassingServiceLevelByPriority()
    {
        var lane = BuildLane();
        var first = BuildServiceLevel("EXPRESS", priority: 1, maxWeightKg: 30m, lanes: [lane]);
        var second = BuildServiceLevel("ECONOMY", priority: 2, maxWeightKg: 30m, lanes: [lane]);
        var profile = BuildProfile(serviceLevels: [second, first]); // intentionally unsorted

        var result = _sut.Evaluate(profile, BuildCheck());

        Assert.Equal("EXPRESS", result.ServiceLevelCode);
    }

    // ---- Helpers ----

    private static CarrierProfileSnapshot BuildProfile(
        CarrierStatus status = CarrierStatus.Active,
        bool requiresRealTime = false,
        bool serviceLevelIsActive = true,
        decimal maxWeightKg = 30m,
        decimal maxCubicWeightKg = 30m,
        bool supportsFragile = true,
        bool supportsRestricted = true,
        IReadOnlySet<string>? blockedCategories = null,
        TimeOnly? cutoffTime = null,
        IReadOnlySet<DayOfWeek>? operatingDays = null,
        IReadOnlyList<CarrierServiceLevelSnapshot>? serviceLevels = null)
    {
        if (serviceLevels is not null)
            return new CarrierProfileSnapshot(Guid.NewGuid(), "CARRIER", status, requiresRealTime, DateTimeOffset.UtcNow, serviceLevels);

        var lane = BuildLane(cutoffTime: cutoffTime, operatingDays: operatingDays);
        var sl = BuildServiceLevel(
            "EXPRESS",
            priority: 1,
            isActive: serviceLevelIsActive,
            maxWeightKg: maxWeightKg,
            maxCubicWeightKg: maxCubicWeightKg,
            supportsFragile: supportsFragile,
            supportsRestricted: supportsRestricted,
            blockedCategories: blockedCategories,
            lanes: [lane]);

        return new CarrierProfileSnapshot(Guid.NewGuid(), "CARRIER", status, requiresRealTime, DateTimeOffset.UtcNow, [sl]);
    }

    private static CarrierServiceLevelSnapshot BuildServiceLevel(
        string code = "EXPRESS",
        int priority = 1,
        bool isActive = true,
        decimal maxWeightKg = 30m,
        decimal maxCubicWeightKg = 30m,
        bool supportsFragile = true,
        bool supportsRestricted = true,
        IReadOnlySet<string>? blockedCategories = null,
        IReadOnlyList<CarrierLaneSnapshot>? lanes = null)
    {
        return new CarrierServiceLevelSnapshot(
            code,
            TransportMode.CARRIER,
            maxWeightKg,
            maxCubicWeightKg,
            supportsFragile,
            supportsRestricted,
            priority,
            isActive,
            blockedCategories ?? new HashSet<string>(),
            lanes ?? [BuildLane()]);
    }

    private static CarrierLaneSnapshot BuildLane(
        TimeOnly? cutoffTime = null,
        IReadOnlySet<DayOfWeek>? operatingDays = null,
        bool isActive = true)
    {
        return new CarrierLaneSnapshot(
            OriginId,
            DestId,
            "UTC",
            cutoffTime ?? new TimeOnly(23, 59),
            operatingDays ?? new HashSet<DayOfWeek>
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday
            },
            isActive);
    }

    private static CarrierAvailabilityCheckRequest BuildCheck(
        string serviceLevelCode = "EXPRESS",
        Guid? originNodeId = null,
        Guid? destinationNodeId = null,
        DateTimeOffset? departure = null,
        PackageProfileDto? package = null)
    {
        return new CarrierAvailabilityCheckRequest(
            "check-1",
            "CARRIER",
            serviceLevelCode,
            originNodeId ?? OriginId,
            destinationNodeId ?? DestId,
            "12345-678",
            departure ?? MondayAt10,
            package ?? BuildPackage());
    }

    private static PackageProfileDto BuildPackage(
        decimal weightKg = 1m,
        decimal cubicWeightKg = 1m,
        bool isFragile = false,
        bool isRestricted = false,
        string? category = null)
    {
        return new PackageProfileDto(weightKg, cubicWeightKg, isFragile, isRestricted, category);
    }
}
