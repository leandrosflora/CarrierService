using System.Text.Json.Serialization;
using CarrierService.Adapters;
using CarrierService.Api;
using CarrierService.Application;
using CarrierService.Application.Ports;
using CarrierService.Infrastructure.Cache;
using CarrierService.Infrastructure.Outbox;
using CarrierService.Infrastructure.Persistence;
using CarrierService.Infrastructure.Workers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var carrierDbConnectionString = builder.Configuration.GetConnectionString("CarrierDb")
    ?? "Host=localhost;Port=5432;Database=carrier_service;Username=postgres;Password=postgres";

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

builder.Services.AddScoped<ICarrierRepository, CarrierRepository>();
builder.Services.AddScoped<ICarrierProfileCache, RedisCarrierProfileCache>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();

builder.Services
    .AddHttpClient<MeliLogisticsAdapter>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Carriers:Meli:BaseUrl"] ?? "http://localhost:8081");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    })
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
        options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(700);
        options.Retry.MaxRetryAttempts = 1;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 10;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);
    });

builder.Services
    .AddHttpClient<ExternalCarrierAdapter>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Carriers:External:BaseUrl"] ?? "http://localhost:8082");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    })
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(3);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
        options.Retry.MaxRetryAttempts = 1;
        options.CircuitBreaker.FailureRatio = 0.4;
        options.CircuitBreaker.MinimumThroughput = 8;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    });

builder.Services.AddTransient<ICarrierAdapter>(provider => provider.GetRequiredService<MeliLogisticsAdapter>());
builder.Services.AddTransient<ICarrierAdapter>(provider => provider.GetRequiredService<ExternalCarrierAdapter>());
builder.Services.AddSingleton<CarrierAdapterFactory>();

builder.Services.AddHostedService<CarrierHealthRefreshWorker>();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddDbContextCheck<CarrierDbContext>(tags: ["ready"]);

var app = builder.Build();

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
