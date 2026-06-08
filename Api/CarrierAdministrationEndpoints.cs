using CarrierService.Application;
using CarrierService.Application.Ports;
using CarrierService.Contracts;
using CarrierService.Domain;
using CarrierService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarrierService.Api;

public static class CarrierAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapCarrierAdministrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/carriers").WithTags("Carrier Administration");

        group.MapGet("/{carrierCode}", async (
            string carrierCode,
            ICarrierRepository repository,
            CancellationToken cancellationToken) =>
        {
            var profile = await repository.GetProfileAsync(carrierCode, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });

        group.MapPatch("/{carrierCode}/status", async (
            string carrierCode,
            ChangeCarrierStatusRequest request,
            CarrierStatusService service,
            CancellationToken cancellationToken) =>
        {
            await service.ChangeStatusAsync(carrierCode, request.Status, request.Reason, cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/{carrierCode}/webhooks/status", async (
            string carrierCode,
            CarrierStatusWebhookRequest request,
            CarrierStatusService service,
            CancellationToken cancellationToken) =>
        {
            var status = Enum.Parse<CarrierStatus>(request.Status, ignoreCase: true);

            await service.ChangeStatusAsync(carrierCode, status, request.Reason, cancellationToken);
            return Results.Accepted();
        });

        group.MapPost("", async (
            CreateCarrierRequest request,
            ICarrierRepository repository,
            IOutboxWriter outbox,
            CancellationToken cancellationToken) =>
        {
            var carrier = new Carrier(request.Code, request.Name, request.RequiresRealTimeValidation);
            await repository.AddAsync(carrier, cancellationToken);

            await outbox.AddAsync(
                "CarrierCreated",
                new
                {
                    CarrierId = carrier.Id,
                    carrier.Code,
                    carrier.Name,
                    carrier.RequiresRealTimeValidation
                },
                cancellationToken);

            await repository.SaveChangesAsync(cancellationToken);
            return Results.Created($"/carriers/{carrier.Code}", carrier);
        });

        group.MapPost("/{carrierCode}/service-levels", async (
            string carrierCode,
            CreateCarrierServiceLevelRequest request,
            CarrierDbContext dbContext,
            ICarrierProfileCache cache,
            IOutboxWriter outbox,
            CancellationToken cancellationToken) =>
        {
            var normalizedCode = carrierCode.Trim().ToUpperInvariant();
            var carrier = await dbContext.Carriers
                .Include(x => x.ServiceLevels)
                .SingleOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);

            if (carrier is null)
                return Results.NotFound();

            var serviceLevel = carrier.AddServiceLevel(
                request.Code,
                request.Name,
                request.Mode,
                request.MaximumWeightKg,
                request.MaximumCubicWeightKg,
                request.SupportsFragileItems,
                request.SupportsRestrictedItems,
                request.Priority);

            await outbox.AddAsync(
                "CarrierServiceLevelChanged",
                new { CarrierId = carrier.Id, carrier.Code, ServiceLevelCode = serviceLevel.Code },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await cache.RemoveAsync(carrier.Code, cancellationToken);

            return Results.Created($"/carriers/{carrier.Code}/service-levels/{serviceLevel.Code}", serviceLevel);
        });

        group.MapPost("/{carrierCode}/lanes", async (
            string carrierCode,
            CreateCarrierLaneRequest request,
            CarrierDbContext dbContext,
            ICarrierProfileCache cache,
            IOutboxWriter outbox,
            CancellationToken cancellationToken) =>
        {
            var normalizedCode = carrierCode.Trim().ToUpperInvariant();
            var normalizedServiceLevelCode = request.ServiceLevelCode.Trim().ToUpperInvariant();
            var carrier = await dbContext.Carriers
                .Include(x => x.ServiceLevels)
                    .ThenInclude(x => x.Lanes)
                .SingleOrDefaultAsync(x => x.Code == normalizedCode, cancellationToken);

            if (carrier is null)
                return Results.NotFound();

            var serviceLevel = carrier.ServiceLevels.SingleOrDefault(x => x.Code == normalizedServiceLevelCode);

            if (serviceLevel is null)
                return Results.NotFound();

            var lane = new CarrierLane(
                serviceLevel.Id,
                request.OriginNodeId,
                request.DestinationNodeId,
                request.TimeZoneId,
                request.CutoffTime,
                request.OperatingDays);

            serviceLevel.Lanes.Add(lane);

            await outbox.AddAsync(
                "CarrierLaneActivated",
                new
                {
                    CarrierId = carrier.Id,
                    carrier.Code,
                    ServiceLevelCode = serviceLevel.Code,
                    lane.OriginNodeId,
                    lane.DestinationNodeId
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await cache.RemoveAsync(carrier.Code, cancellationToken);

            return Results.Created($"/carriers/{carrier.Code}/lanes/{lane.Id}", lane);
        });

        return app;
    }
}
