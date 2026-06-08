using System.Net.Http.Json;

namespace CarrierService.Adapters;

public sealed class MeliLogisticsAdapter : ICarrierAdapter
{
    private readonly HttpClient _httpClient;

    public string CarrierCode => "MELI-LOGISTICS";

    public MeliLogisticsAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ExternalCarrierAvailabilityResult>> CheckAvailabilityAsync(
        IReadOnlyList<ExternalCarrierAvailabilityRequest> checks,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/internal/logistics/availability",
            new { checks },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<List<ExternalCarrierAvailabilityResult>>(cancellationToken);

        return result ?? [];
    }

    public async Task<CarrierHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("/health/ready", cancellationToken);

        return new CarrierHealthResult(
            response.IsSuccessStatusCode,
            response.IsSuccessStatusCode ? null : response.StatusCode.ToString());
    }
}
