using CarrierService.Adapters;
using CarrierService.Application;
using CarrierService.Application.Models;
using CarrierService.Application.Ports;
using CarrierService.Contracts;
using CarrierService.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace CarrierService.UnitTests;

public class CarrierAvailabilityServiceTests
{
    private readonly ICarrierRepository _repository = Substitute.For<ICarrierRepository>();
    private readonly ICarrierProfileCache _cache = Substitute.For<ICarrierProfileCache>();
    private readonly CarrierStaticRuleEvaluator _evaluator = new();

    private static readonly Guid OriginId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DestId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset MondayAt10 = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    private CarrierAvailabilityService CreateService(ICarrierAdapter? adapter = null)
    {
        ICarrierAdapter[] adapters = adapter is null ? [] : [adapter];
        var factory = new CarrierAdapterFactory(adapters);
        return new CarrierAvailabilityService(
            _repository, _cache, _evaluator, factory,
            NullLogger<CarrierAvailabilityService>.Instance);
    }

    [Fact]
    public async Task SearchAsync_WhenChecksEmpty_ThrowsArgumentException()
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SearchAsync(new SearchCarrierAvailabilityRequest([]), default));
    }

    [Fact]
    public async Task SearchAsync_WhenMoreThan20Checks_ThrowsArgumentException()
    {
        var checks = Enumerable.Range(1, 21).Select(i => BuildCheck($"c{i}")).ToList();
        var sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SearchAsync(new SearchCarrierAvailabilityRequest(checks), default));
    }

    [Fact]
    public async Task SearchAsync_WhenDuplicateCheckIds_ThrowsArgumentException()
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("dup"), BuildCheck("dup")]), default));
    }

    [Fact]
    public async Task SearchAsync_WhenEmptyCheckId_ThrowsArgumentException()
    {
        var sut = CreateService();
        var bad = new CarrierAvailabilityCheckRequest(
            "", "CARRIER", "EXPRESS", OriginId, DestId, "12345", MondayAt10,
            new PackageProfileDto(1m, 1m, false, false, null));

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SearchAsync(new SearchCarrierAvailabilityRequest([bad]), default));
    }

    [Fact]
    public async Task SearchAsync_WhenCarrierNotFound_ReturnsCarrierNotFoundResult()
    {
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CarrierProfileSnapshot?)null);
        _repository.GetProfileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CarrierProfileSnapshot?)null);
        var sut = CreateService();

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("c1")]), default);

        Assert.Single(response.Results);
        Assert.False(response.Results[0].Available);
        Assert.Equal(CarrierAvailabilityReason.CarrierNotFound.ToString(), response.Results[0].ReasonCode);
        Assert.Equal("Local", response.Results[0].Source);
    }

    [Fact]
    public async Task SearchAsync_WhenCacheHit_DoesNotCallRepository()
    {
        var profile = BuildAvailableProfile();
        _cache.GetAsync("CARRIER", Arg.Any<CancellationToken>()).Returns(profile);
        var sut = CreateService();

        await sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("c1")]), default);

        await _repository.DidNotReceive().GetProfileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_WhenCacheMiss_FetchesFromRepositoryAndStoresInCache()
    {
        var profile = BuildAvailableProfile();
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CarrierProfileSnapshot?)null);
        _repository.GetProfileAsync("CARRIER", Arg.Any<CancellationToken>()).Returns(profile);
        var sut = CreateService();

        await sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("c1")]), default);

        await _cache.Received(1).SetAsync(profile, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_WhenCacheThrows_StillFetchesFromRepository()
    {
        var profile = BuildAvailableProfile();
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Redis down"));
        _repository.GetProfileAsync("CARRIER", Arg.Any<CancellationToken>()).Returns(profile);
        var sut = CreateService();

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("c1")]), default);

        Assert.True(response.Results[0].Available);
    }

    [Fact]
    public async Task SearchAsync_WhenLocalRulePasses_NoRealTime_ReturnsLocalAvailableResult()
    {
        var profile = BuildAvailableProfile(requiresRealTime: false);
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);
        var sut = CreateService();

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("c1")]), default);

        var result = response.Results[0];
        Assert.True(result.Available);
        Assert.Equal("Local", result.Source);
        Assert.NotNull(result.ValidUntil);
    }

    [Fact]
    public async Task SearchAsync_WhenRealTimeRequired_NoAdapterRegistered_ReturnsPartnerUnavailable()
    {
        var profile = BuildAvailableProfile(requiresRealTime: true);
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);
        var sut = CreateService(adapter: null);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("c1")]), default);

        var result = response.Results[0];
        Assert.False(result.Available);
        Assert.Equal(CarrierAvailabilityReason.PartnerUnavailable.ToString(), result.ReasonCode);
        Assert.Equal("External", result.Source);
    }

    [Fact]
    public async Task SearchAsync_WhenRealTimeRequired_AdapterApproves_ReturnsExternalAvailableResult()
    {
        var profile = BuildAvailableProfile(requiresRealTime: true);
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);

        var adapter = Substitute.For<ICarrierAdapter>();
        adapter.CarrierCode.Returns("CARRIER");
        adapter.CheckAvailabilityAsync(
                Arg.Any<IReadOnlyList<ExternalCarrierAvailabilityRequest>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<ExternalCarrierAvailabilityResult>
            {
                new("c1", true, null, DateTimeOffset.UtcNow.AddHours(1))
            });
        var sut = CreateService(adapter);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("c1")]), default);

        var result = response.Results[0];
        Assert.True(result.Available);
        Assert.Equal("External", result.Source);
        Assert.NotNull(result.ValidUntil);
    }

    [Fact]
    public async Task SearchAsync_WhenRealTimeRequired_AdapterRejects_ReturnsUnavailableWithRejectionCode()
    {
        var profile = BuildAvailableProfile(requiresRealTime: true);
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);

        var adapter = Substitute.For<ICarrierAdapter>();
        adapter.CarrierCode.Returns("CARRIER");
        adapter.CheckAvailabilityAsync(
                Arg.Any<IReadOnlyList<ExternalCarrierAvailabilityRequest>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<ExternalCarrierAvailabilityResult>
            {
                new("c1", false, "CAPACITY_FULL", null)
            });
        var sut = CreateService(adapter);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("c1")]), default);

        var result = response.Results[0];
        Assert.False(result.Available);
        Assert.Equal("CAPACITY_FULL", result.ReasonCode);
        Assert.Equal("External", result.Source);
    }

    [Fact]
    public async Task SearchAsync_WhenRealTimeRequired_AdapterThrows_ReturnsPartnerUnavailable()
    {
        var profile = BuildAvailableProfile(requiresRealTime: true);
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);

        var adapter = Substitute.For<ICarrierAdapter>();
        adapter.CarrierCode.Returns("CARRIER");
        adapter.CheckAvailabilityAsync(
                Arg.Any<IReadOnlyList<ExternalCarrierAvailabilityRequest>>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("timeout"));
        var sut = CreateService(adapter);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([BuildCheck("c1")]), default);

        Assert.False(response.Results[0].Available);
        Assert.Equal(CarrierAvailabilityReason.PartnerUnavailable.ToString(), response.Results[0].ReasonCode);
    }

    [Fact]
    public async Task SearchAsync_NormalizesCarrierCodeToUpperInvariant()
    {
        var profile = BuildAvailableProfile();
        // cache keyed on normalized code
        _cache.GetAsync("CARRIER", Arg.Any<CancellationToken>()).Returns(profile);
        var sut = CreateService();

        var check = new CarrierAvailabilityCheckRequest(
            "c1", "carrier", "EXPRESS", OriginId, DestId, "12345", MondayAt10,
            new PackageProfileDto(1m, 1m, false, false, null));
        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([check]), default);

        Assert.True(response.Results[0].Available);
    }

    [Fact]
    public async Task SearchAsync_ResultsAreOrderedByCheckId()
    {
        var profile = BuildAvailableProfile();
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);
        var sut = CreateService();

        var checks = new List<CarrierAvailabilityCheckRequest>
        {
            BuildCheck("z-last"),
            BuildCheck("a-first"),
            BuildCheck("m-middle"),
        };
        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest(checks), default);

        Assert.Equal("a-first", response.Results[0].CheckId);
        Assert.Equal("m-middle", response.Results[1].CheckId);
        Assert.Equal("z-last", response.Results[2].CheckId);
    }

    // ---- Helpers ----

    private static CarrierProfileSnapshot BuildAvailableProfile(bool requiresRealTime = false)
    {
        var lane = new CarrierLaneSnapshot(OriginId, DestId, "UTC", new TimeOnly(23, 59),
            new HashSet<DayOfWeek>
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday
            },
            true);

        var sl = new CarrierServiceLevelSnapshot(
            "EXPRESS", TransportMode.CARRIER, 30m, 30m, true, true, 1, true,
            new HashSet<string>(), [lane]);

        return new CarrierProfileSnapshot(
            Guid.NewGuid(), "CARRIER", CarrierStatus.Active,
            requiresRealTime, DateTimeOffset.UtcNow, [sl]);
    }

    private static CarrierAvailabilityCheckRequest BuildCheck(string checkId)
    {
        return new CarrierAvailabilityCheckRequest(
            checkId, "CARRIER", "EXPRESS", OriginId, DestId,
            "12345-678", MondayAt10,
            new PackageProfileDto(1m, 1m, false, false, null));
    }
}
