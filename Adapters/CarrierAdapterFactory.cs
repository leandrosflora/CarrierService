namespace CarrierService.Adapters;

public sealed class CarrierAdapterFactory
{
    private readonly IReadOnlyDictionary<string, ICarrierAdapter> _adapters;

    public CarrierAdapterFactory(IEnumerable<ICarrierAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(x => x.CarrierCode, StringComparer.OrdinalIgnoreCase);
    }

    public ICarrierAdapter GetRequired(string carrierCode)
    {
        if (!_adapters.TryGetValue(carrierCode, out var adapter))
            throw new InvalidOperationException($"No adapter registered for carrier {carrierCode}");

        return adapter;
    }

    public bool TryGet(string carrierCode, out ICarrierAdapter? adapter)
    {
        return _adapters.TryGetValue(carrierCode, out adapter);
    }
}
