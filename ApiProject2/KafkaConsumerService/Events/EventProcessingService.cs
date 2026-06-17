using System.Text.Json;

namespace KafkaConsumerService.Events;

public class EventProcessingService : IEventProcessingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<EventProcessingService> _logger;

    public EventProcessingService(ILogger<EventProcessingService> logger)
    {
        _logger = logger;
    }

    public Task ProcessAsync(string rawMessage)
    {
        var envelope = JsonSerializer.Deserialize<KafkaEventEnvelope>(rawMessage, JsonOptions);
        if (envelope is null)
        {
            _logger.LogWarning("Could not deserialize Kafka envelope — skipping. Raw: {Raw}", rawMessage);
            return Task.CompletedTask;
        }

        switch (envelope.EventType)
        {
            case "OrderCreated":
                ProcessOrderCreated(envelope);
                break;

            case "LotteryDrawn":
                ProcessLotteryDrawn(envelope);
                break;

            default:
                _logger.LogWarning("Unknown event type {EventType} — skipping.", envelope.EventType);
                break;
        }

        return Task.CompletedTask;
    }

    private void ProcessOrderCreated(KafkaEventEnvelope envelope)
    {
        var order = envelope.Data.Deserialize<OrderCreatedEvent>(JsonOptions);
        if (order is null)
        {
            _logger.LogWarning("Could not deserialize OrderCreated payload — skipping.");
            return;
        }

        _logger.LogInformation(
            "OrderCreated [{OccurredAt}] — Cart {CartId} for User {UserId}: {ItemCount} item(s), Total {TotalAmount}",
            envelope.OccurredAt, order.CartId, order.UserId, order.Items.Count, order.TotalAmount
        );

        foreach (var item in order.Items)
        {
            _logger.LogInformation(
                "  - Gift {GiftId} '{GiftName}' x{Quantity} @ {TicketPrice}",
                item.GiftId, item.GiftName, item.Quantity, item.TicketPrice
            );
        }
    }

    private void ProcessLotteryDrawn(KafkaEventEnvelope envelope)
    {
        var lottery = envelope.Data.Deserialize<LotteryDrawnEvent>(JsonOptions);
        if (lottery is null)
        {
            _logger.LogWarning("Could not deserialize LotteryDrawn payload — skipping.");
            return;
        }

        _logger.LogInformation(
            "LotteryDrawn [{OccurredAt}] — Gift {GiftId} '{GiftName}' won by {WinnerName} (User {WinnerUserId}). " +
            "Donor: {DonorName}, Tickets sold: {TotalTicketsSold}, Participants: {TotalParticipants}, Income: {TotalIncome}",
            envelope.OccurredAt, lottery.GiftId, lottery.GiftName, lottery.WinnerName, lottery.WinnerUserId,
            lottery.DonorName, lottery.TotalTicketsSold, lottery.TotalParticipants, lottery.TotalIncome
        );
    }
}
