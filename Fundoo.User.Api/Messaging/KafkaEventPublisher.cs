using System.Text.Json;
using Confluent.Kafka;

namespace Fundoo.User.Api.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<T>(string topic, string key, T message, CancellationToken ct = default);
}

public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IProducer<string, string> _producer;

    public KafkaEventPublisher(IConfiguration config)
    {
        var cfg = new ProducerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"] ?? "localhost:9092",
            Acks = Acks.All,
            EnableIdempotence = true,
            // Without this, a stopped Kafka makes register hang for ~5 minutes
            MessageTimeoutMs = 5000,
            SocketTimeoutMs = 5000
        };
        _producer = new ProducerBuilder<string, string>(cfg).Build();
    }

    public async Task PublishAsync<T>(string topic, string key, T message, CancellationToken ct = default)
    {
        await _producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = key,
            Value = JsonSerializer.Serialize(message, Json)
        }, ct);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}