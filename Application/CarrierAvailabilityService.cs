using CarrierService.Adapters;
using CarrierService.Application.Models;
using CarrierService.Application.Ports;
using CarrierService.Contracts;
using CarrierService.Domain;

namespace CarrierService.Application;

public sealed class CarrierAvailabilityService
{
    private static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromSeconds(30);

    private readonly ICarrierRepository _repository;
    private readonly ICarrierProfileCache _cache;
    private readonly CarrierStaticRuleEvaluator _ruleEvaluator;
    private readonly CarrierAdapterFactory _adapterFactory;
    private readonly ILogger<CarrierAvailabilityService> _logger;

    public CarrierAvailabilityService(
        ICarrierRepository repository,
        ICarrierProfileCache cache,
        CarrierStaticRuleEvaluator ruleEvaluator,
        CarrierAdapterFactory adapterFactory,
        ILogger<CarrierAvailabilityService> logger)
    {
        _repository = repository;
        _cache = cache;
        _ruleEvaluator = ruleEvaluator;
        _adapterFactory = adapterFactory;
        _logger = logger;
    }

    public async Task<SearchCarrierAvailabilityResponse> SearchAsync(
        SearchCarrierAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var tasks = request.Checks
            .GroupBy(x => x.CarrierCode.Trim().ToUpperInvariant())
            .Select(group => ProcessCarrierGroupAsync(group.Key, group.ToList(), cancellationToken));

        var groupedResults = await Task.WhenAll(tasks);

        var results = groupedResults
            .SelectMany(x => x)
            .OrderBy(x => x.CheckId)
            .ToList();

        return new SearchCarrierAvailabilityResponse(results);
    }

    private async Task<IReadOnlyList<CarrierAvailabilityResult>> ProcessCarrierGroupAsync(
        string carrierCode,
        IReadOnlyList<CarrierAvailabilityCheckRequest> checks,
        CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(carrierCode, cancellationToken);

        if (profile is null)
        {
            return checks
                .Select(x => Unavailable(x, CarrierAvailabilityReason.CarrierNotFound, "Local"))
                .ToList();
        }

        var localResults = new Dictionary<string, CarrierAvailabilityResult>(StringComparer.OrdinalIgnoreCase);
        var externalChecks = new List<ExternalCarrierAvailabilityRequest>();

        foreach (var check in checks)
        {
            var evaluation = _ruleEvaluator.Evaluate(profile, check);

            if (!evaluation.Available)
            {
                localResults[check.CheckId] = Unavailable(check, evaluation.Reason, "Local");
                continue;
            }

            if (!evaluation.RequiresRealTimeValidation)
            {
                var evaluatedAt = DateTimeOffset.UtcNow;
                localResults[check.CheckId] = new CarrierAvailabilityResult(
                    check.CheckId,
                    carrierCode,
                    evaluation.ServiceLevelCode,
                    true,
                    CarrierAvailabilityReason.Available.ToString(),
                    "Local",
                    evaluatedAt,
                    evaluatedAt.AddSeconds(30));

                continue;
            }

            externalChecks.Add(new ExternalCarrierAvailabilityRequest(
                check.CheckId,
                evaluation.ServiceLevelCode!,
                check.OriginNodeId,
                check.DestinationNodeId,
                check.DestinationPostalCode,
                check.PlannedDepartureAtUtc,
                check.Package));
        }

        if (externalChecks.Count == 0)
            return localResults.Values.ToList();

        if (!_adapterFactory.TryGet(carrierCode, out var adapter) || adapter is null)
        {
            foreach (var check in externalChecks)
            {
                var original = checks.Single(x => x.CheckId == check.CheckId);
                localResults[check.CheckId] = Unavailable(
                    original,
                    CarrierAvailabilityReason.PartnerUnavailable,
                    "External");
            }

            return localResults.Values.ToList();
        }

        try
        {
            var externalResults = await adapter.CheckAvailabilityAsync(externalChecks, cancellationToken);
            var externalById = externalResults.ToDictionary(x => x.CheckId, StringComparer.OrdinalIgnoreCase);

            foreach (var check in externalChecks)
            {
                var original = checks.Single(x => x.CheckId == check.CheckId);

                if (!externalById.TryGetValue(check.CheckId, out var externalResult))
                {
                    localResults[check.CheckId] = Unavailable(
                        original,
                        CarrierAvailabilityReason.PartnerUnavailable,
                        "External");
                    continue;
                }

                localResults[check.CheckId] = new CarrierAvailabilityResult(
                    check.CheckId,
                    carrierCode,
                    check.ServiceLevelCode,
                    externalResult.Available,
                    externalResult.Available
                        ? CarrierAvailabilityReason.Available.ToString()
                        : externalResult.RejectionCode ?? CarrierAvailabilityReason.PartnerRejected.ToString(),
                    "External",
                    DateTimeOffset.UtcNow,
                    externalResult.ValidUntil);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Carrier {CarrierCode} availability call failed", carrierCode);

            foreach (var check in externalChecks)
            {
                var original = checks.Single(x => x.CheckId == check.CheckId);
                localResults[check.CheckId] = Unavailable(
                    original,
                    CarrierAvailabilityReason.PartnerUnavailable,
                    "External");
            }
        }

        return localResults.Values.ToList();
    }

    private async Task<CarrierProfileSnapshot?> GetProfileAsync(
        string carrierCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var cached = await _cache.GetAsync(carrierCode, cancellationToken);

            if (cached is not null)
                return cached;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not read carrier profile from cache");
        }

        var profile = await _repository.GetProfileAsync(carrierCode, cancellationToken);

        if (profile is null)
            return null;

        try
        {
            await _cache.SetAsync(profile, ProfileCacheTtl, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not store carrier profile in cache");
        }

        return profile;
    }

    private static CarrierAvailabilityResult Unavailable(
        CarrierAvailabilityCheckRequest check,
        CarrierAvailabilityReason reason,
        string source)
    {
        return new CarrierAvailabilityResult(
            check.CheckId,
            check.CarrierCode,
            check.ServiceLevelCode,
            false,
            reason.ToString(),
            source,
            DateTimeOffset.UtcNow,
            null);
    }

    private static void Validate(SearchCarrierAvailabilityRequest request)
    {
        if (request.Checks.Count == 0)
            throw new ArgumentException("At least one check is required");

        if (request.Checks.Count > 20)
            throw new ArgumentException("A maximum of 20 checks is allowed");

        var duplicatedIds = request.Checks
            .GroupBy(x => x.CheckId)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        if (duplicatedIds.Count > 0)
            throw new ArgumentException("CheckId must be unique inside the request");

        foreach (var check in request.Checks)
        {
            if (string.IsNullOrWhiteSpace(check.CheckId))
                throw new ArgumentException("CheckId is required");

            if (string.IsNullOrWhiteSpace(check.CarrierCode))
                throw new ArgumentException("CarrierCode is required");

            if (string.IsNullOrWhiteSpace(check.ServiceLevelCode))
                throw new ArgumentException("ServiceLevelCode is required");

            if (check.OriginNodeId == Guid.Empty || check.DestinationNodeId == Guid.Empty)
                throw new ArgumentException("Origin and destination nodes are required");

            if (check.Package.WeightKg <= 0)
                throw new ArgumentException("Weight must be positive");

            if (check.Package.CubicWeightKg < 0)
                throw new ArgumentException("Cubic weight cannot be negative");
        }
    }
}
