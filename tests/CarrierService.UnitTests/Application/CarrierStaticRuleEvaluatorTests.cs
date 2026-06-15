using CarrierService.Application;
using CarrierService.Application.Models;
using CarrierService.Contracts;
using CarrierService.Domain;

namespace CarrierService.UnitTests.Application;

public sealed class CarrierStaticRuleEvaluatorTests
{
    private static readonly Guid Origin = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Destination = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly CarrierStaticRuleEvaluator _sut = new();

    [Fact]
    public void Evaluate_WhenCarrierIsSuspended_ReturnsCarrierSuspendedWithoutRealtimeValidation()
    {
        var profile = Profile(status: CarrierStatus.Suspended);
        var result = _sut.Evaluate(profile, ValidCheck());

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.CarrierSuspended, result.Reason);
        Assert.Null(result.ServiceLevelCode);
        Assert.False(result.RequiresRealTimeValidation);
    }

    [Fact]
    public void Evaluate_WhenMatchingActiveLaneAndPackage_ReturnsAvailableServiceLevelByPriority()
    {
        var lowPriority = ServiceLevel("STANDARD", priority: 20);
        var highPriority = ServiceLevel("EXPRESS", priority: 10);
        var profile = Profile(requiresRealTimeValidation: true, serviceLevels: [lowPriority, highPriority]);

        var result = _sut.Evaluate(profile, ValidCheck(serviceLevelCode: null));

        Assert.True(result.Available);
        Assert.Equal(CarrierAvailabilityReason.Available, result.Reason);
        Assert.Equal("EXPRESS", result.ServiceLevelCode);
        Assert.True(result.RequiresRealTimeValidation);
    }

    [Fact]
    public void Evaluate_WhenRequestedServiceLevelDoesNotExist_ReturnsServiceLevelNotFound()
    {
        var result = _sut.Evaluate(Profile(), ValidCheck(serviceLevelCode: "SAME-DAY"));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.ServiceLevelNotFound, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenLaneDoesNotMatch_ReturnsLaneNotSupported()
    {
        var result = _sut.Evaluate(Profile(), ValidCheck(destination: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.LaneNotSupported, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenPackageExceedsWeight_ReturnsWeightExceeded()
    {
        var result = _sut.Evaluate(Profile(), ValidCheck(package: new PackageProfileDto(31m, 1m, false, false, null)));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.WeightExceeded, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenCategoryIsBlocked_ReturnsCategoryUnsupported()
    {
        var result = _sut.Evaluate(Profile(), ValidCheck(package: new PackageProfileDto(1m, 1m, false, false, "electronics")));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.CategoryUnsupported, result.Reason);
    }

    [Fact]
    public void Evaluate_WhenDepartureIsAfterCutoff_ReturnsOutsideOperatingWindow()
    {
        var result = _sut.Evaluate(Profile(), ValidCheck(plannedDepartureAtUtc: new DateTimeOffset(2026, 6, 15, 22, 0, 0, TimeSpan.Zero)));

        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.OutsideOperatingWindow, result.Reason);
    }

    private static CarrierProfileSnapshot Profile(
        CarrierStatus status = CarrierStatus.Active,
        bool requiresRealTimeValidation = false,
        IReadOnlyList<CarrierServiceLevelSnapshot>? serviceLevels = null) =>
        new(Guid.NewGuid(), "MELI-LOGISTICS", status, requiresRealTimeValidation, DateTimeOffset.UtcNow, serviceLevels ?? [ServiceLevel("STANDARD", 10)]);

    private static CarrierServiceLevelSnapshot ServiceLevel(string code, int priority) =>
        new(code, TransportMode.Road, 30m, 50m, true, true, priority, true, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "electronics" },
            [new CarrierLaneSnapshot(Origin, Destination, "UTC", new TimeOnly(18, 0), new HashSet<DayOfWeek> { DayOfWeek.Monday }, true)]);

    private static CarrierAvailabilityCheckRequest ValidCheck(
        string? serviceLevelCode = "STANDARD",
        Guid? destination = null,
        DateTimeOffset? plannedDepartureAtUtc = null,
        PackageProfileDto? package = null) =>
        new("check-1", "MELI-LOGISTICS", serviceLevelCode, Origin, destination ?? Destination, "01001-000",
            plannedDepartureAtUtc ?? new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
            package ?? new PackageProfileDto(1m, 1m, false, false, null));
}
