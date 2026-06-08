using CarrierService.Application.Models;
using CarrierService.Domain;

namespace CarrierService.Application.Ports;

public interface ICarrierRepository
{
    Task<CarrierProfileSnapshot?> GetProfileAsync(string carrierCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<Carrier>> GetCarriersRequiringHealthCheckAsync(CancellationToken cancellationToken);

    Task<Carrier?> GetByCodeAsync(string carrierCode, CancellationToken cancellationToken);

    Task AddAsync(Carrier carrier, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
