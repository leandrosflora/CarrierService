namespace CarrierService.Application.Ports;

public interface IOutboxWriter
{
    Task AddAsync(string eventType, object payload, CancellationToken cancellationToken);
}
