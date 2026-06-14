using CarrierService.Application.Models;
using CarrierService.Application.Ports;
using CarrierService.Domain;

namespace CarrierService.Infrastructure.Persistence;

public sealed class MockCarrierRepository : ICarrierRepository
{
    private static readonly Guid MeliCarrierId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExternalCarrierId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OriginNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DestinationNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlySet<DayOfWeek> EveryDay = Enum.GetValues<DayOfWeek>().ToHashSet();

    private static readonly IReadOnlyDictionary<string, CarrierProfileSnapshot> Profiles =
        new Dictionary<string, CarrierProfileSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["MELI"] = CreateProfile(MeliCarrierId, "MELI", requiresRealTimeValidation: false),
            ["EXTERNAL"] = CreateProfile(ExternalCarrierId, "EXTERNAL", requiresRealTimeValidation: false)
        };

    public Task<CarrierProfileSnapshot?> GetProfileAsync(
        string carrierCode,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeCode(carrierCode);
        Profiles.TryGetValue(normalizedCode, out var profile);

        return Task.FromResult(profile);
    }

    public Task<IReadOnlyList<Carrier>> GetCarriersRequiringHealthCheckAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Carrier>>([]);
    }

    public Task<Carrier?> GetByCodeAsync(string carrierCode, CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeCode(carrierCode);

        if (!Profiles.ContainsKey(normalizedCode))
            return Task.FromResult<Carrier?>(null);

        return Task.FromResult<Carrier?>(new Carrier(normalizedCode, $"Mock {normalizedCode}", requiresRealTimeValidation: false));
    }

    public Task AddAsync(Carrier carrier, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static CarrierProfileSnapshot CreateProfile(
        Guid carrierId,
        string code,
        bool requiresRealTimeValidation)
    {
        return new CarrierProfileSnapshot(
            carrierId,
            code,
            CarrierStatus.Active,
            requiresRealTimeValidation,
            SeededAt,
            [
                new CarrierServiceLevelSnapshot(
                    "STANDARD",
                    TransportMode.Road,
                    MaximumWeightKg: 30,
                    MaximumCubicWeightKg: 50,
                    SupportsFragileItems: true,
                    SupportsRestrictedItems: false,
                    Priority: 1,
                    IsActive: true,
                    BlockedCategories: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "hazmat"
                    },
                    Lanes:
                    [
                        new CarrierLaneSnapshot(
                            OriginNodeId,
                            DestinationNodeId,
                            "UTC",
                            new TimeOnly(23, 59),
                            EveryDay,
                            IsActive: true)
                    ]),
                new CarrierServiceLevelSnapshot(
                    "EXPRESS",
                    TransportMode.Air,
                    MaximumWeightKg: 10,
                    MaximumCubicWeightKg: 20,
                    SupportsFragileItems: true,
                    SupportsRestrictedItems: false,
                    Priority: 2,
                    IsActive: true,
                    BlockedCategories: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "hazmat"
                    },
                    Lanes:
                    [
                        new CarrierLaneSnapshot(
                            OriginNodeId,
                            DestinationNodeId,
                            "UTC",
                            new TimeOnly(18, 0),
                            EveryDay,
                            IsActive: true)
                    ])
            ]);
    }

    private static string NormalizeCode(string carrierCode)
    {
        return carrierCode.Trim().ToUpperInvariant();
    }
}
