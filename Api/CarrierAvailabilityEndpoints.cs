using CarrierService.Application;
using CarrierService.Contracts;

namespace CarrierService.Api;

public static class CarrierAvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapCarrierAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/carrier-availability").WithTags("Carrier Availability");

        group.MapPost("/search", async (
            SearchCarrierAvailabilityRequest request,
            CarrierAvailabilityService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.SearchAsync(request, cancellationToken);
            return Results.Ok(response);
        });

        return app;
    }
}
