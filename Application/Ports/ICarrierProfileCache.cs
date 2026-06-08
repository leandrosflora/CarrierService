using CarrierService.Application.Models;

namespace CarrierService.Application.Ports;

public interface ICarrierProfileCache
{
    Task<CarrierProfileSnapshot?> GetAsync(string carrierCode, CancellationToken cancellationToken);

    Task SetAsync(CarrierProfileSnapshot profile, TimeSpan ttl, CancellationToken cancellationToken);

    Task RemoveAsync(string carrierCode, CancellationToken cancellationToken);
}
