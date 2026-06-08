using CarrierService.Application.Models;
using CarrierService.Application.Ports;
using CarrierService.Domain;
using Microsoft.EntityFrameworkCore;

namespace CarrierService.Infrastructure.Persistence;

public sealed class CarrierRepository : ICarrierRepository
{
    private readonly CarrierDbContext _dbContext;

    public CarrierRepository(CarrierDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CarrierProfileSnapshot?> GetProfileAsync(
        string carrierCode,
        CancellationToken cancellationToken)
    {
        var normalizedCode = carrierCode.Trim().ToUpperInvariant();

        var carrier = await _dbContext.Carriers
            .AsNoTracking()
            .Include(x => x.ServiceLevels)
                .ThenInclude(x => x.Lanes)
            .Include(x => x.ServiceLevels)
                .ThenInclude(x => x.CategoryRestrictions)
            .SingleOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);

        if (carrier is null)
            return null;

        return new CarrierProfileSnapshot(
            carrier.Id,
            carrier.Code,
            carrier.Status,
            carrier.RequiresRealTimeValidation,
            carrier.StatusUpdatedAt,
            carrier.ServiceLevels
                .Select(service => new CarrierServiceLevelSnapshot(
                    service.Code,
                    service.Mode,
                    service.MaximumWeightKg,
                    service.MaximumCubicWeightKg,
                    service.SupportsFragileItems,
                    service.SupportsRestrictedItems,
                    service.Priority,
                    service.IsActive,
                    service.CategoryRestrictions
                        .Where(x => x.IsBlocked)
                        .Select(x => x.Category)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase),
                    service.Lanes
                        .Select(lane => new CarrierLaneSnapshot(
                            lane.OriginNodeId,
                            lane.DestinationNodeId,
                            lane.TimeZoneId,
                            lane.CutoffTime,
                            GetOperatingDays(lane),
                            lane.IsActive))
                        .ToList()))
                .ToList());
    }

    public async Task<IReadOnlyList<Carrier>> GetCarriersRequiringHealthCheckAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Carriers
            .Where(x => x.RequiresRealTimeValidation)
            .Where(x => x.Status != CarrierStatus.Inactive)
            .ToListAsync(cancellationToken);
    }

    public Task<Carrier?> GetByCodeAsync(string carrierCode, CancellationToken cancellationToken)
    {
        var normalizedCode = carrierCode.Trim().ToUpperInvariant();

        return _dbContext.Carriers.SingleOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);
    }

    public async Task AddAsync(Carrier carrier, CancellationToken cancellationToken)
    {
        await _dbContext.Carriers.AddAsync(carrier, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlySet<DayOfWeek> GetOperatingDays(CarrierLane lane)
    {
        var days = new HashSet<DayOfWeek>();

        if (lane.OperatesOnMonday)
            days.Add(DayOfWeek.Monday);

        if (lane.OperatesOnTuesday)
            days.Add(DayOfWeek.Tuesday);

        if (lane.OperatesOnWednesday)
            days.Add(DayOfWeek.Wednesday);

        if (lane.OperatesOnThursday)
            days.Add(DayOfWeek.Thursday);

        if (lane.OperatesOnFriday)
            days.Add(DayOfWeek.Friday);

        if (lane.OperatesOnSaturday)
            days.Add(DayOfWeek.Saturday);

        if (lane.OperatesOnSunday)
            days.Add(DayOfWeek.Sunday);

        return days;
    }
}
