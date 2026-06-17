using ApiProject.Services.Interface;
using Confluent.Kafka;
using System.Text.Json;

namespace ApiProject.Services.Implement
{
    public class KafkaProducerService : IKafkaProducerService, IDisposable
    {
        private readonly IProducer<Null, string> _producer;
        private readonly ILogger<KafkaProducerService> _logger;
        private readonly string _topic;

        public KafkaProducerService(IConfiguration configuration, ILogger<KafkaProducerService> logger)
        {
            _logger = logger;
            _topic = configuration["KafkaSettings:Topic"] ?? "gift-lottery-events";

            var config = new ProducerConfig
            {
                BootstrapServers = configuration["KafkaSettings:BootstrapServers"]
            };

            _producer = new ProducerBuilder<Null, string>(config).Build();
        }

        public async Task PublishAsync(string eventType, object payload)
        {
            try
            {
                _logger.LogInformation("Publishing Kafka event {EventType} to topic {Topic}", eventType, _topic);

                var message = JsonSerializer.Serialize(new
                {
                    eventType,
                    occurredAt = DateTime.UtcNow,
                    data = payload
                });

                var result = await _producer.ProduceAsync(_topic, new Message<Null, string> { Value = message });

                _logger.LogInformation(
                    "Published Kafka event {EventType} to {Topic} [partition {Partition}, offset {Offset}]",
                    eventType, result.Topic, result.Partition.Value, result.Offset.Value
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish Kafka event {EventType}", eventType);
            }
        }

        public void Dispose()
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
            _producer.Dispose();
        }
    }
}
