namespace CarrierService.Domain;

public sealed class CarrierCategoryRestriction
{
    public Guid Id { get; private set; }
    public Guid CarrierServiceLevelId { get; private set; }

    public string Category { get; private set; } = default!;

    public bool IsBlocked { get; private set; }

    private CarrierCategoryRestriction()
    {
    }

    public CarrierCategoryRestriction(Guid carrierServiceLevelId, string category, bool isBlocked)
    {
        if (carrierServiceLevelId == Guid.Empty)
            throw new ArgumentException("CarrierServiceLevelId is required", nameof(carrierServiceLevelId));

        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category is required", nameof(category));

        Id = Guid.NewGuid();
        CarrierServiceLevelId = carrierServiceLevelId;
        Category = category.Trim().ToLowerInvariant();
        IsBlocked = isBlocked;
    }
}
