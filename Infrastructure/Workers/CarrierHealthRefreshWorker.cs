using CarrierService.Adapters;
using CarrierService.Application;
using CarrierService.Application.Ports;
using CarrierService.Domain;

namespace CarrierService.Infrastructure.Workers;

public sealed class CarrierHealthRefreshWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CarrierAdapterFactory _adapterFactory;
    private readonly ILogger<CarrierHealthRefreshWorker> _logger;

    public CarrierHealthRefreshWorker(
        IServiceScopeFactory scopeFactory,
        CarrierAdapterFactory adapterFactory,
        ILogger<CarrierHealthRefreshWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _adapterFactory = adapterFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not refresh carrier health");
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<ICarrierRepository>();
        var statusService = scope.ServiceProvider.GetRequiredService<CarrierStatusService>();

        var carriers = await repository.GetCarriersRequiringHealthCheckAsync(cancellationToken);

        foreach (var carrier in carriers)
        {
            if (!_adapterFactory.TryGet(carrier.Code, out var adapter) || adapter is null)
                continue;

            try
            {
                var health = await adapter.CheckHealthAsync(cancellationToken);
                var desiredStatus = health.Healthy ? CarrierStatus.Active : CarrierStatus.Degraded;

                await statusService.ChangeStatusAsync(
                    carrier.Code,
                    desiredStatus,
                    health.Reason ?? "Health reconciliation",
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Carrier health check failed for {CarrierCode}", carrier.Code);

                await statusService.ChangeStatusAsync(
                    carrier.Code,
                    CarrierStatus.Degraded,
                    "Carrier health endpoint unavailable",
                    cancellationToken);
            }
        }
    }
}
