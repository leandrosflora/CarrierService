namespace CarrierService.Domain;

public sealed class Carrier
{
    public Guid Id { get; private set; }

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    public CarrierStatus Status { get; private set; }

    public bool RequiresRealTimeValidation { get; private set; }

    public DateTimeOffset StatusUpdatedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public List<CarrierServiceLevel> ServiceLevels { get; private set; } = [];

    private Carrier()
    {
    }

    public Carrier(string code, string name, bool requiresRealTimeValidation)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Carrier code is required", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Carrier name is required", nameof(name));

        Id = Guid.NewGuid();
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        RequiresRealTimeValidation = requiresRealTimeValidation;
        Status = CarrierStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        StatusUpdatedAt = CreatedAt;
    }

    public void ChangeStatus(CarrierStatus status)
    {
        Status = status;
        StatusUpdatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = StatusUpdatedAt;
    }

    public CarrierServiceLevel AddServiceLevel(
        string code,
        string name,
        TransportMode mode,
        decimal maximumWeightKg,
        decimal maximumCubicWeightKg,
        bool supportsFragileItems,
        bool supportsRestrictedItems,
        int priority)
    {
        var serviceLevel = new CarrierServiceLevel(
            Id,
            code,
            name,
            mode,
            maximumWeightKg,
            maximumCubicWeightKg,
            supportsFragileItems,
            supportsRestrictedItems,
            priority);

        ServiceLevels.Add(serviceLevel);
        UpdatedAt = DateTimeOffset.UtcNow;
        return serviceLevel;
    }
}
