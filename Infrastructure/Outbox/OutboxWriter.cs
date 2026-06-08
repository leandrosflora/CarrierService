using System.Text.Json;
using System.Text.Json.Serialization;
using CarrierService.Application.Ports;
using CarrierService.Infrastructure.Persistence;

namespace CarrierService.Infrastructure.Outbox;

public sealed class OutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CarrierDbContext _dbContext;

    public OutboxWriter(CarrierDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(string eventType, object payload, CancellationToken cancellationToken)
    {
        var envelope = new
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            OccurredAt = DateTimeOffset.UtcNow,
            Payload = payload
        };

        var message = new OutboxMessage(eventType, JsonSerializer.Serialize(envelope, JsonOptions));
        _dbContext.OutboxMessages.Add(message);

        return Task.CompletedTask;
    }
}
