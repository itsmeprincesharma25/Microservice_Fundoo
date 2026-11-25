using System.Net.Sockets;
using Confluent.Kafka;
using Fundoo.User.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Fundoo.User.Infrastructure.Kafka;

public class KafkaProducer : IKafkaProducer
{
    private readonly string _bootstrapServers;
    private readonly ProducerConfig _config;
    private readonly ILogger<KafkaProducer> _logger;
    private readonly object _lock = new();
    private IProducer<string, string>? _producer;

    public KafkaProducer(IConfiguration configuration, ILogger<KafkaProducer> logger)
    {
        _logger = logger;
        _bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        _config = new ProducerConfig
        {
            BootstrapServers = _bootstrapServers,
            Acks = Acks.Leader,
            MessageTimeoutMs = 3000
        };
    }

    public async Task ProduceAsync(string topic, string key, string message, CancellationToken ct = default)
    {
        if (!await IsBrokerReachableAsync(_bootstrapServers, ct))
        {
            _logger.LogDebug("Kafka broker not reachable at {Servers}; skipping event on topic {Topic}.", _bootstrapServers, topic);
            return;
        }

        try
        {
            var producer = GetOrCreateProducer();
            if (producer == null) return;

            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = key,
                Value = message
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send Kafka message to topic {Topic}: {Message}", topic, ex.Message);
        }
    }

    private IProducer<string, string>? GetOrCreateProducer()
    {
        if (_producer != null) return _producer;

        lock (_lock)
        {
            if (_producer != null) return _producer;
            try
            {
                _producer = new ProducerBuilder<string, string>(_config).Build();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kafka producer could not be initialized: {Message}", ex.Message);
            }
            return _producer;
        }
    }

    private static async Task<bool> IsBrokerReachableAsync(string bootstrapServers, CancellationToken ct)
    {
        foreach (var endpoint in bootstrapServers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = endpoint.Split(':');
            var host = parts[0];
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 9092;

            try
            {
                using var tcp = new TcpClient();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(300));
                await tcp.ConnectAsync(host, port, timeoutCts.Token);
                return true;
            }
            catch
            {
                // Try next configured broker
            }
        }

        return false;
    }
}
