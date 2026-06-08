namespace CarrierService.Domain;

public sealed class CarrierServiceLevel
{
    public Guid Id { get; private set; }
    public Guid CarrierId { get; private set; }

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    public TransportMode Mode { get; private set; }

    public decimal MaximumWeightKg { get; private set; }
    public decimal MaximumCubicWeightKg { get; private set; }

    public bool SupportsFragileItems { get; private set; }
    public bool SupportsRestrictedItems { get; private set; }

    public int Priority { get; private set; }
    public bool IsActive { get; private set; }

    public List<CarrierLane> Lanes { get; private set; } = [];
    public List<CarrierCategoryRestriction> CategoryRestrictions { get; private set; } = [];

    private CarrierServiceLevel()
    {
    }

    public CarrierServiceLevel(
        Guid carrierId,
        string code,
        string name,
        TransportMode mode,
        decimal maximumWeightKg,
        decimal maximumCubicWeightKg,
        bool supportsFragileItems,
        bool supportsRestrictedItems,
        int priority)
    {
        if (carrierId == Guid.Empty)
            throw new ArgumentException("CarrierId is required", nameof(carrierId));

        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Service level code is required", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Service level name is required", nameof(name));

        if (maximumWeightKg <= 0)
            throw new ArgumentException("Maximum weight must be positive", nameof(maximumWeightKg));

        if (maximumCubicWeightKg < 0)
            throw new ArgumentException("Maximum cubic weight cannot be negative", nameof(maximumCubicWeightKg));

        Id = Guid.NewGuid();
        CarrierId = carrierId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Mode = mode;
        MaximumWeightKg = maximumWeightKg;
        MaximumCubicWeightKg = maximumCubicWeightKg;
        SupportsFragileItems = supportsFragileItems;
        SupportsRestrictedItems = supportsRestrictedItems;
        Priority = priority;
        IsActive = true;
    }

    public bool SupportsPackage(decimal weightKg, decimal cubicWeightKg, bool isFragile, bool isRestricted)
    {
        if (!IsActive)
            return false;

        if (weightKg > MaximumWeightKg)
            return false;

        if (cubicWeightKg > MaximumCubicWeightKg)
            return false;

        if (isFragile && !SupportsFragileItems)
            return false;

        if (isRestricted && !SupportsRestrictedItems)
            return false;

        return true;
    }
}
