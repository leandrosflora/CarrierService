using CarrierService.Application.Models;
using CarrierService.Contracts;
using CarrierService.Domain;

namespace CarrierService.Application;

public sealed record StaticCarrierEvaluation(
    bool Available,
    CarrierAvailabilityReason Reason,
    string? ServiceLevelCode,
    bool RequiresRealTimeValidation);

public sealed class CarrierStaticRuleEvaluator
{
    public StaticCarrierEvaluation Evaluate(CarrierProfileSnapshot profile, CarrierAvailabilityCheckRequest check)
    {
        if (profile.Status is CarrierStatus.Suspended or CarrierStatus.Maintenance or CarrierStatus.Inactive)
            return Unavailable(CarrierAvailabilityReason.CarrierSuspended);

        if (profile.Status is CarrierStatus.Degraded)
            return Unavailable(CarrierAvailabilityReason.CarrierDegraded);

        var serviceLevels = profile.ServiceLevels.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(check.ServiceLevelCode))
        {
            serviceLevels = serviceLevels.Where(x =>
                string.Equals(x.Code, check.ServiceLevelCode, StringComparison.OrdinalIgnoreCase));
        }

        var orderedServices = serviceLevels.OrderBy(x => x.Priority).ToList();

        if (orderedServices.Count == 0)
            return Unavailable(CarrierAvailabilityReason.ServiceLevelNotFound);

        foreach (var service in orderedServices)
        {
            var packageFailure = ValidatePackage(service, check.Package);

            if (packageFailure is not null)
                continue;

            var lane = service.Lanes.FirstOrDefault(x =>
                x.IsActive
                && x.OriginNodeId == check.OriginNodeId
                && x.DestinationNodeId == check.DestinationNodeId);

            if (lane is null)
                continue;

            if (!OperatesAt(lane, check.PlannedDepartureAtUtc))
                continue;

            return new StaticCarrierEvaluation(
                true,
                CarrierAvailabilityReason.Available,
                service.Code,
                profile.RequiresRealTimeValidation);
        }

        return DetermineFailure(profile, check);
    }

    private static CarrierAvailabilityReason? ValidatePackage(
        CarrierServiceLevelSnapshot service,
        PackageProfileDto package)
    {
        if (package.WeightKg > service.MaximumWeightKg)
            return CarrierAvailabilityReason.WeightExceeded;

        if (package.CubicWeightKg > service.MaximumCubicWeightKg)
            return CarrierAvailabilityReason.CubicWeightExceeded;

        if (package.IsFragile && !service.SupportsFragileItems)
            return CarrierAvailabilityReason.FragileItemUnsupported;

        if (package.IsRestricted && !service.SupportsRestrictedItems)
            return CarrierAvailabilityReason.RestrictedItemUnsupported;

        if (!string.IsNullOrWhiteSpace(package.Category)
            && service.BlockedCategories.Contains(package.Category, StringComparer.OrdinalIgnoreCase))
        {
            return CarrierAvailabilityReason.CategoryUnsupported;
        }

        return null;
    }

    private static bool OperatesAt(CarrierLaneSnapshot lane, DateTimeOffset plannedDepartureUtc)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(lane.TimeZoneId);
        var localDeparture = TimeZoneInfo.ConvertTime(plannedDepartureUtc, timeZone);

        return lane.OperatingDays.Contains(localDeparture.DayOfWeek)
            && TimeOnly.FromDateTime(localDeparture.DateTime) <= lane.CutoffTime;
    }

    private static StaticCarrierEvaluation DetermineFailure(
        CarrierProfileSnapshot profile,
        CarrierAvailabilityCheckRequest check)
    {
        var filteredServices = profile.ServiceLevels.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(check.ServiceLevelCode))
        {
            filteredServices = filteredServices.Where(x =>
                string.Equals(x.Code, check.ServiceLevelCode, StringComparison.OrdinalIgnoreCase));
        }

        var services = filteredServices.ToList();

        if (services.Count == 0)
            return Unavailable(CarrierAvailabilityReason.ServiceLevelNotFound);

        var hasLane = services.SelectMany(x => x.Lanes).Any(x =>
            x.OriginNodeId == check.OriginNodeId
            && x.DestinationNodeId == check.DestinationNodeId);

        if (!hasLane)
            return Unavailable(CarrierAvailabilityReason.LaneNotSupported);

        var packageReasons = services
            .Select(x => ValidatePackage(x, check.Package))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        if (packageReasons.Count > 0)
            return Unavailable(packageReasons[0]);

        return Unavailable(CarrierAvailabilityReason.OutsideOperatingWindow);
    }

    private static StaticCarrierEvaluation Unavailable(CarrierAvailabilityReason reason)
    {
        return new StaticCarrierEvaluation(false, reason, null, false);
    }
}
