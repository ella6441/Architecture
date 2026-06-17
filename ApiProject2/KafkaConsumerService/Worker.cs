using Confluent.Kafka;
using KafkaConsumerService.Events;

namespace KafkaConsumerService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IEventProcessingService _eventProcessingService;

    public Worker(ILogger<Worker> logger, IConfiguration configuration, IEventProcessingService eventProcessingService)
    {
        _logger = logger;
        _configuration = configuration;
        _eventProcessingService = eventProcessingService;
    }

    // Confluent.Kafka's Consume() is a blocking call — run the loop on a background
    // thread via Task.Run so it never blocks the host's startup thread.
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(async () =>
        {
            var topic = _configuration["KafkaSettings:Topic"] ?? "gift-lottery-events";

            var config = new ConsumerConfig
            {
                BootstrapServers = _configuration["KafkaSettings:BootstrapServers"],
                GroupId = _configuration["KafkaSettings:GroupId"] ?? "gift-lottery-consumer-group",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            consumer.Subscribe(topic);

            _logger.LogInformation("Kafka consumer started — listening on topic {Topic}", topic);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<Ignore, string>? result = null;

                    try
                    {
                        result = consumer.Consume(stoppingToken);
                    }
                    catch (ConsumeException ex)
                    {
                        // e.g. topic doesn't exist yet because no one has published to it — keep retrying
                        _logger.LogWarning(ex, "Kafka consume error, retrying: {Reason}", ex.Error.Reason);
                        Thread.Sleep(2000);
                        continue;
                    }

                    if (result is null) continue;

                    try
                    {
                        await _eventProcessingService.ProcessAsync(result.Message.Value);
                        consumer.Commit(result);
                    }
                    catch (Exception ex)
                    {
                        // Don't commit on failure — the message will be redelivered.
                        _logger.LogError(ex, "Error processing message at offset {Offset} — not committing.", result.Offset);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected on graceful shutdown
            }
            finally
            {
                consumer.Close();
            }
        }, stoppingToken);
    }
}
