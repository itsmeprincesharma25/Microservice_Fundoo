using System.Text.Json;
using Confluent.Kafka;
using Fundoo.Notes.Application.Abstractions;

namespace Fundoo.Notes.Api.Messaging;

public sealed class UserRegisteredConsumer : BackgroundService
{
    private const string Topic = "user.registered";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] DefaultLabels = ["Personal", "Work", "Ideas"];

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<UserRegisteredConsumer> _logger;

    public UserRegisteredConsumer(
        IServiceScopeFactory scopes,
        IConfiguration config,
        ILogger<UserRegisteredConsumer> logger)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    // Task.Run so the blocking Consume() loop doesn't stop the app from starting
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => RunAsync(stoppingToken), stoppingToken);

    private async Task RunAsync(CancellationToken ct)
    {
        var cfg = new ConsumerConfig
        {
            BootstrapServers = _config["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = _config["Kafka:GroupId"] ?? "notes-service",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(cfg).Build();
        consumer.Subscribe(Topic);
        _logger.LogInformation("Kafka consumer subscribed to {Topic}", Topic);

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(ct);
                if (result?.Message is null) continue;

                var evt = JsonSerializer.Deserialize<UserRegisteredEvent>(result.Message.Value, Json);
                if (evt is not null)
                    await HandleAsync(evt, ct);

                consumer.Commit(result);   // commit only after successful processing
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (JsonException ex)
            {
                // Unreadable message: skip it, otherwise it would block the topic forever
                _logger.LogError(ex, "Skipping malformed message");
                if (result is not null) consumer.Commit(result);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message; will retry");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        consumer.Close();
    }

    private async Task HandleAsync(UserRegisteredEvent evt, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var labels = scope.ServiceProvider.GetRequiredService<ILabelService>();

        foreach (var name in DefaultLabels)
        {
            // A "Duplicate" outcome is fine, so processing the same event twice is harmless
            await labels.CreateAsync(evt.UserId, name, ct);
        }

        _logger.LogInformation("Created default labels for user {UserId}", evt.UserId);
    }
}