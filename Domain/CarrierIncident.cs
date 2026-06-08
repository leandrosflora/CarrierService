namespace CarrierService.Domain;

public sealed class CarrierIncident
{
    public Guid Id { get; private set; }
    public Guid CarrierId { get; private set; }
    public string IncidentType { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private CarrierIncident()
    {
    }
}
