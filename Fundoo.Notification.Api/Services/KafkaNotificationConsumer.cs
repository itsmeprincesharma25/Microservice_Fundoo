using System.Net.Sockets;
using System.Text.Json;
using Confluent.Kafka;
using Fundoo.Notification.Application.Abstractions;

namespace Fundoo.Notification.Api.Services;

public class KafkaNotificationConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaNotificationConsumer> _logger;

    public KafkaNotificationConsumer(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<KafkaNotificationConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var groupId = _configuration["Kafka:GroupId"] ?? "notification-service";
        var topic = _configuration["Kafka:Topic"] ?? "user-events";

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        var loggedOffline = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await IsBrokerReachableAsync(bootstrapServers, stoppingToken))
            {
                if (!loggedOffline)
                {
                    _logger.LogInformation(
                        "Kafka broker not reachable at {Servers}; HTTP endpoints remain active and Kafka consumer will check every 30s.",
                        bootstrapServers);
                    loggedOffline = true;
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            loggedOffline = false;
            _logger.LogInformation("Starting Kafka Notification Consumer on topic {Topic}...", topic);

            try
            {
                using var consumer = new ConsumerBuilder<string, string>(config).Build();
                consumer.Subscribe(topic);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(2));
                        if (consumeResult == null) continue;

                        _logger.LogInformation("Received Kafka event: {Key}", consumeResult.Message.Key);

                        using var scope = _serviceProvider.CreateScope();
                        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                        var jsonDoc = JsonDocument.Parse(consumeResult.Message.Value);
                        var root = jsonDoc.RootElement;

                        var eventType = root.GetProperty("eventType").GetString();
                        var email = root.GetProperty("email").GetString() ?? "";
                        var fullName = root.TryGetProperty("fullName", out var nameProp) ? nameProp.GetString() : "User";
                        var payload = root.TryGetProperty("payload", out var payProp) ? payProp.GetString() : "";

                        if (eventType == "UserRegistered" && !string.IsNullOrWhiteSpace(email))
                        {
                            await notificationService.SendEmailAsync(
                                email,
                                "Welcome to Fundoo Notes!",
                                $"Hello {fullName},\n\nWelcome to Fundoo Notes! Your account has been registered successfully.",
                                false,
                                stoppingToken);
                        }
                        else if (eventType == "TwoFactorRequested" && !string.IsNullOrWhiteSpace(email))
                        {
                            await notificationService.SendEmailAsync(
                                email,
                                "Your 2FA Verification Code",
                                $"Hello {fullName},\n\nYour two-factor verification code is: {payload}\nThis code will expire in 10 minutes.",
                                false,
                                stoppingToken);
                        }
                        else if (eventType == "ForgotPasswordRequested" && !string.IsNullOrWhiteSpace(email))
                        {
                            await notificationService.SendEmailAsync(
                                email,
                                "Fundoo Notes - Password Reset Request",
                                $"Hello {fullName},\n\nYou recently requested to reset your password for your Fundoo Notes account.\n\nYour Password Reset Token is:\n{payload}\n\nPlease use this token in the Reset Password form to set your new password. This token will expire in 15 minutes.\n\nIf you did not request a password reset, please ignore this email or secure your account.",
                                false,
                                stoppingToken);
                        }
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogWarning("Kafka consume warning: {Reason}", ex.Error.Reason);
                        break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing Kafka notification message.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Kafka consumer could not connect to {Servers}: {Message}", bootstrapServers, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
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
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));
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
