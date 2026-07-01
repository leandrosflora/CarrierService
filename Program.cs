using System.Text.Json.Serialization;
using CarrierService.Adapters;
using CarrierService.Api;
using CarrierService.Application;
using CarrierService.Application.Ports;
using CarrierService.Infrastructure.Cache;
using CarrierService.Infrastructure.Outbox;
using CarrierService.Infrastructure.Persistence;
using CarrierService.Infrastructure.Serialization;
using CarrierService.Infrastructure.Workers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var serviceName = builder.Environment.ApplicationName;
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:5107";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new TimeOnlyHHmmConverter());
});

var carrierDbConnectionString = builder.Configuration.GetConnectionString("CarrierDb")
    ?? "Host=localhost;Port=5432;Database=logistica_envios;Username=logistica;Password=logistica;Search Path=carrier,public";

builder.Services.AddDbContext<CarrierDbContext>(options =>
{
    options.UseNpgsql(carrierDbConnectionString);
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    options.InstanceName = "carrier:";
});

builder.Services.AddScoped<CarrierAvailabilityService>();
builder.Services.AddScoped<CarrierStaticRuleEvaluator>();
builder.Services.AddScoped<CarrierStatusService>();

if (builder.Configuration.GetValue<bool>("FeatureFlags:MockCarrierRepository"))
{
    builder.Services.AddScoped<ICarrierRepository, MockCarrierRepository>();
}
else
{
    builder.Services.AddScoped<ICarrierRepository, CarrierRepository>();
}
builder.Services.AddScoped<ICarrierProfileCache, RedisCarrierProfileCache>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();

builder.Services
    .AddHttpClient<MeliLogisticsAdapter>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Carriers:Meli:BaseUrl"] ?? "http://localhost:8081");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    })
    .AddStandardResilienceHandler(options => ConfigureStandardResilience(
        options,
        totalRequestTimeout: TimeSpan.FromSeconds(60),
        attemptTimeout: TimeSpan.FromSeconds(20),
        maxRetryAttempts: 1,
        failureRatio: 0.5,
        minimumThroughput: 10,
        samplingDuration: TimeSpan.FromSeconds(60),
        breakDuration: TimeSpan.FromSeconds(20)));

builder.Services
    .AddHttpClient<ExternalCarrierAdapter>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Carriers:External:BaseUrl"] ?? "http://localhost:8082");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    })
    .AddStandardResilienceHandler(options => ConfigureStandardResilience(
        options,
        totalRequestTimeout: TimeSpan.FromSeconds(30),
        attemptTimeout: TimeSpan.FromSeconds(20),
        maxRetryAttempts: 1,
        failureRatio: 0.4,
        minimumThroughput: 8,
        samplingDuration: TimeSpan.FromSeconds(60),
        breakDuration: TimeSpan.FromSeconds(60)));

builder.Services.AddTransient<ICarrierAdapter>(provider => provider.GetRequiredService<MeliLogisticsAdapter>());
builder.Services.AddTransient<ICarrierAdapter>(provider => provider.GetRequiredService<ExternalCarrierAdapter>());
builder.Services.AddSingleton<CarrierAdapterFactory>();

builder.Services.AddHostedService<CarrierHealthRefreshWorker>();

var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

if (!builder.Configuration.GetValue<bool>("FeatureFlags:MockCarrierRepository"))
{
    healthChecks.AddDbContextCheck<CarrierDbContext>(tags: ["ready"]);
}

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health");

app.MapCarrierAvailabilityEndpoints();
app.MapCarrierAdministrationEndpoints();

app.Run();

void ConfigureStandardResilience(
    HttpStandardResilienceOptions options,
    TimeSpan totalRequestTimeout,
    TimeSpan attemptTimeout,
    int maxRetryAttempts,
    double failureRatio,
    int minimumThroughput,
    TimeSpan samplingDuration,
    TimeSpan breakDuration)
{
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(totalRequestTimeout, attemptTimeout);

    if (samplingDuration < attemptTimeout * 2)
    {
        throw new ArgumentOutOfRangeException(
            nameof(samplingDuration),
            samplingDuration,
            "Circuit breaker sampling duration must be at least double the attempt timeout.");
    }

    options.TotalRequestTimeout.Timeout = totalRequestTimeout;
    options.AttemptTimeout.Timeout = attemptTimeout;
    options.Retry.MaxRetryAttempts = maxRetryAttempts;
    options.CircuitBreaker.FailureRatio = failureRatio;
    options.CircuitBreaker.MinimumThroughput = minimumThroughput;
    options.CircuitBreaker.SamplingDuration = samplingDuration;
    options.CircuitBreaker.BreakDuration = breakDuration;
}
