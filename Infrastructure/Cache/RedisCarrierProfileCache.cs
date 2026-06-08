using System.Text.Json;
using System.Text.Json.Serialization;
using CarrierService.Application.Models;
using CarrierService.Application.Ports;
using Microsoft.Extensions.Caching.Distributed;

namespace CarrierService.Infrastructure.Cache;

public sealed class RedisCarrierProfileCache : ICarrierProfileCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDistributedCache _cache;

    public RedisCarrierProfileCache(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<CarrierProfileSnapshot?> GetAsync(string carrierCode, CancellationToken cancellationToken)
    {
        var json = await _cache.GetStringAsync(BuildKey(carrierCode), cancellationToken);

        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<CarrierProfileSnapshot>(json, JsonOptions);
    }

    public Task SetAsync(CarrierProfileSnapshot profile, TimeSpan ttl, CancellationToken cancellationToken)
    {
        return _cache.SetStringAsync(
            BuildKey(profile.Code),
            JsonSerializer.Serialize(profile, JsonOptions),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            },
            cancellationToken);
    }

    public Task RemoveAsync(string carrierCode, CancellationToken cancellationToken)
    {
        return _cache.RemoveAsync(BuildKey(carrierCode), cancellationToken);
    }

    private static string BuildKey(string carrierCode)
    {
        return $"profile:{carrierCode.Trim().ToUpperInvariant()}";
    }
}
