namespace ApiProject.Services.Interface
{
    public interface IKafkaProducerService
    {
        Task PublishAsync(string eventType, object payload);
    }
}
