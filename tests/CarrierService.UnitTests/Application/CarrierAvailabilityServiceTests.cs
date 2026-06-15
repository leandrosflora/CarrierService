using CarrierService.Adapters;
using CarrierService.Application;
using CarrierService.Application.Models;
using CarrierService.Application.Ports;
using CarrierService.Contracts;
using CarrierService.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarrierService.UnitTests.Application;

public sealed class CarrierAvailabilityServiceTests
{
    private static readonly Guid Origin = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Destination = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task SearchAsync_WhenRequestHasNoChecks_ThrowsValidationError()
    {
        var sut = CreateService(new FakeRepository(Profile()), new FakeCache(), []);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync(new SearchCarrierAvailabilityRequest([]), CancellationToken.None));

        Assert.Equal("At least one check is required", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_WhenCarrierDoesNotExist_ReturnsCarrierNotFoundFromLocalSource()
    {
        var sut = CreateService(new FakeRepository(null), new FakeCache(), []);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([ValidCheck()]), CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.False(result.Available);
        Assert.Equal("CarrierNotFound", result.ReasonCode);
        Assert.Equal("Local", result.Source);
    }

    [Fact]
    public async Task SearchAsync_WhenStaticRulesPassWithoutRealtimeValidation_ReturnsLocalAvailabilityAndCachesProfile()
    {
        var cache = new FakeCache();
        var sut = CreateService(new FakeRepository(Profile(requiresRealTimeValidation: false)), cache, []);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([ValidCheck()]), CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.True(result.Available);
        Assert.Equal("Available", result.ReasonCode);
        Assert.Equal("Local", result.Source);
        Assert.Equal("STANDARD", result.ServiceLevelCode);
        Assert.NotNull(result.ValidUntil);
        Assert.Equal(1, cache.SetCalls);
    }

    [Fact]
    public async Task SearchAsync_WhenProfileIsCached_DoesNotQueryRepository()
    {
        var repository = new FakeRepository(Profile()) { ThrowOnGetProfile = true };
        var sut = CreateService(repository, new FakeCache(Profile()), []);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([ValidCheck()]), CancellationToken.None);

        Assert.True(Assert.Single(response.Results).Available);
        Assert.Equal(0, repository.GetProfileCalls);
    }

    [Fact]
    public async Task SearchAsync_WhenRealtimeAdapterAccepts_ReturnsExternalAvailability()
    {
        var adapter = new StubCarrierAdapter("MELI-LOGISTICS")
        {
            Results = [new ExternalCarrierAvailabilityResult("check-1", true, null, new DateTimeOffset(2026, 6, 15, 13, 0, 0, TimeSpan.Zero))]
        };
        var sut = CreateService(new FakeRepository(Profile(requiresRealTimeValidation: true)), new FakeCache(), [adapter]);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([ValidCheck()]), CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.True(result.Available);
        Assert.Equal("External", result.Source);
        Assert.Equal("Available", result.ReasonCode);
        Assert.Single(adapter.ReceivedChecks);
    }

    [Fact]
    public async Task SearchAsync_WhenRealtimeAdapterRejects_UsesPartnerRejectionCode()
    {
        var adapter = new StubCarrierAdapter("MELI-LOGISTICS")
        {
            Results = [new ExternalCarrierAvailabilityResult("check-1", false, "OUT_OF_CAPACITY", null)]
        };
        var sut = CreateService(new FakeRepository(Profile(requiresRealTimeValidation: true)), new FakeCache(), [adapter]);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([ValidCheck()]), CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.False(result.Available);
        Assert.Equal("OUT_OF_CAPACITY", result.ReasonCode);
        Assert.Equal("External", result.Source);
    }

    [Fact]
    public async Task SearchAsync_WhenRealtimeAdapterIsMissing_ReturnsPartnerUnavailableFromAdapterMissing()
    {
        var sut = CreateService(new FakeRepository(Profile(requiresRealTimeValidation: true)), new FakeCache(), []);

        var response = await sut.SearchAsync(new SearchCarrierAvailabilityRequest([ValidCheck()]), CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.False(result.Available);
        Assert.Equal("PartnerUnavailable", result.ReasonCode);
        Assert.Equal("AdapterMissing", result.Source);
    }

    private static CarrierAvailabilityService CreateService(FakeRepository repository, FakeCache cache, IReadOnlyList<ICarrierAdapter> adapters) =>
        new(repository, cache, new CarrierStaticRuleEvaluator(), new CarrierAdapterFactory(adapters), NullLogger<CarrierAvailabilityService>.Instance);

    private static CarrierProfileSnapshot Profile(bool requiresRealTimeValidation = false) =>
        new(Guid.NewGuid(), "MELI-LOGISTICS", CarrierStatus.Active, requiresRealTimeValidation, DateTimeOffset.UtcNow,
            [new CarrierServiceLevelSnapshot("STANDARD", TransportMode.Road, 30m, 50m, true, true, 10, true, new HashSet<string>(),
                [new CarrierLaneSnapshot(Origin, Destination, "UTC", new TimeOnly(18, 0), new HashSet<DayOfWeek> { DayOfWeek.Monday }, true)])]);

    private static CarrierAvailabilityCheckRequest ValidCheck() =>
        new("check-1", "MELI-LOGISTICS", "STANDARD", Origin, Destination, "01001-000",
            new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero), new PackageProfileDto(1m, 1m, false, false, null));

    private sealed class FakeRepository : ICarrierRepository
    {
        private readonly CarrierProfileSnapshot? _profile;
        public int GetProfileCalls { get; private set; }
        public bool ThrowOnGetProfile { get; init; }

        public FakeRepository(CarrierProfileSnapshot? profile) => _profile = profile;

        public Task<CarrierProfileSnapshot?> GetProfileAsync(string carrierCode, CancellationToken cancellationToken)
        {
            GetProfileCalls++;
            if (ThrowOnGetProfile)
                throw new InvalidOperationException("Repository should not be queried");
            return Task.FromResult(_profile);
        }

        public Task<IReadOnlyList<Carrier>> GetCarriersRequiringHealthCheckAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Carrier>>([]);
        public Task<Carrier?> GetByCodeAsync(string carrierCode, CancellationToken cancellationToken) => Task.FromResult<Carrier?>(null);
        public Task AddAsync(Carrier carrier, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCache : ICarrierProfileCache
    {
        private readonly CarrierProfileSnapshot? _profile;
        public int SetCalls { get; private set; }

        public FakeCache(CarrierProfileSnapshot? profile = null) => _profile = profile;

        public Task<CarrierProfileSnapshot?> GetAsync(string carrierCode, CancellationToken cancellationToken) => Task.FromResult(_profile);
        public Task SetAsync(CarrierProfileSnapshot profile, TimeSpan ttl, CancellationToken cancellationToken) { SetCalls++; return Task.CompletedTask; }
        public Task RemoveAsync(string carrierCode, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubCarrierAdapter : ICarrierAdapter
    {
        public StubCarrierAdapter(string carrierCode) => CarrierCode = carrierCode;
        public string CarrierCode { get; }
        public IReadOnlyList<ExternalCarrierAvailabilityResult> Results { get; init; } = [];
        public List<ExternalCarrierAvailabilityRequest> ReceivedChecks { get; } = [];

        public Task<IReadOnlyList<ExternalCarrierAvailabilityResult>> CheckAvailabilityAsync(IReadOnlyList<ExternalCarrierAvailabilityRequest> checks, CancellationToken cancellationToken)
        {
            ReceivedChecks.AddRange(checks);
            return Task.FromResult(Results);
        }

        public Task<CarrierHealthResult> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new CarrierHealthResult(true, null));
    }
}
