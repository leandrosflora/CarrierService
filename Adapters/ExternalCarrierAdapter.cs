using System.Net.Http.Json;
using CarrierService.Contracts;

namespace CarrierService.Adapters;

public sealed class ExternalCarrierAdapter : ICarrierAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalCarrierAdapter> _logger;

    public string CarrierCode => "EXTERNAL-CARRIER";

    public ExternalCarrierAdapter(HttpClient httpClient, ILogger<ExternalCarrierAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExternalCarrierAvailabilityResult>> CheckAvailabilityAsync(
        IReadOnlyList<ExternalCarrierAvailabilityRequest> checks,
        CancellationToken cancellationToken)
    {
        var providerRequest = new ExternalProviderRequest(
            checks.Select(x => new ExternalProviderCheck(
                    x.CheckId,
                    x.ServiceLevelCode,
                    x.OriginNodeId.ToString(),
                    x.DestinationNodeId.ToString(),
                    NormalizePostalCode(x.DestinationPostalCode),
                    x.PlannedDepartureAtUtc,
                    (int)Math.Ceiling(x.Package.WeightKg * 1000),
                    (int)Math.Ceiling(x.Package.CubicWeightKg * 1000),
                    x.Package.IsFragile))
                .ToList());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/serviceability/batch")
        {
            Content = JsonContent.Create(providerRequest)
        };

        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Carrier {CarrierCode} returned {StatusCode}", CarrierCode, response.StatusCode);
            throw new HttpRequestException($"Carrier returned {response.StatusCode}");
        }

        var providerResponse = await response.Content.ReadFromJsonAsync<ExternalProviderResponse>(cancellationToken);

        if (providerResponse is null)
            throw new InvalidOperationException("Carrier returned an empty response");

        return providerResponse.Results
            .Select(x => new ExternalCarrierAvailabilityResult(
                x.Reference,
                x.Serviceable,
                x.Reason,
                x.ValidUntil))
            .ToList();
    }

    public async Task<CarrierHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("/health", cancellationToken);

        return response.IsSuccessStatusCode
            ? new CarrierHealthResult(true, null)
            : new CarrierHealthResult(false, response.StatusCode.ToString());
    }

    private static string NormalizePostalCode(string postalCode)
    {
        return new string(postalCode.Where(char.IsDigit).ToArray());
    }

    private sealed record ExternalProviderRequest(IReadOnlyList<ExternalProviderCheck> Checks);

    private sealed record ExternalProviderCheck(
        string Reference,
        string ProductCode,
        string OriginUnit,
        string DestinationUnit,
        string PostalCode,
        DateTimeOffset DispatchDate,
        int WeightGrams,
        int CubicWeightGrams,
        bool Fragile);

    private sealed record ExternalProviderResponse(IReadOnlyList<ExternalProviderResult> Results);

    private sealed record ExternalProviderResult(
        string Reference,
        bool Serviceable,
        string? Reason,
        DateTimeOffset? ValidUntil);
}
