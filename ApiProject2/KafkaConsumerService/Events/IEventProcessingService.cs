namespace KafkaConsumerService.Events;

public interface IEventProcessingService
{
    Task ProcessAsync(string rawMessage);
}
