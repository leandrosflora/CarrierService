using CarrierService.Application.Ports;
using CarrierService.Domain;
using CarrierService.Infrastructure.Persistence;

namespace CarrierService.Application;

public sealed class CarrierStatusService
{
    private readonly CarrierDbContext _dbContext;
    private readonly ICarrierRepository _repository;
    private readonly ICarrierProfileCache _cache;
    private readonly IOutboxWriter _outbox;

    public CarrierStatusService(
        CarrierDbContext dbContext,
        ICarrierRepository repository,
        ICarrierProfileCache cache,
        IOutboxWriter outbox)
    {
        _dbContext = dbContext;
        _repository = repository;
        _cache = cache;
        _outbox = outbox;
    }

    public async Task ChangeStatusAsync(
        string carrierCode,
        CarrierStatus newStatus,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var carrier = await _repository.GetByCodeAsync(carrierCode, cancellationToken)
            ?? throw new KeyNotFoundException("Carrier not found");

        if (carrier.Status == newStatus)
            return;

        var previousStatus = carrier.Status;
        carrier.ChangeStatus(newStatus);

        await _outbox.AddAsync(
            "CarrierStatusChanged",
            new
            {
                CarrierId = carrier.Id,
                carrier.Code,
                PreviousStatus = previousStatus,
                NewStatus = newStatus,
                Reason = reason,
                carrier.StatusUpdatedAt
            },
            cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _cache.RemoveAsync(carrier.Code, cancellationToken);
    }
}
