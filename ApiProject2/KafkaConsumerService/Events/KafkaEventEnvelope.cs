using System.Text.Json;

namespace KafkaConsumerService.Events;

public class KafkaEventEnvelope
{
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public JsonElement Data { get; set; }
}

public class OrderCreatedEvent
{
    public int CartId { get; set; }
    public int UserId { get; set; }
    public List<OrderCreatedItem> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
}

public class OrderCreatedItem
{
    public int GiftId { get; set; }
    public string GiftName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int TicketPrice { get; set; }
}

public class LotteryDrawnEvent
{
    public int GiftId { get; set; }
    public string GiftName { get; set; } = string.Empty;
    public int WinnerUserId { get; set; }
    public string WinnerName { get; set; } = string.Empty;
    public string? WinnerEmail { get; set; }
    public string DonorName { get; set; } = string.Empty;
    public int TotalTicketsSold { get; set; }
    public int TotalParticipants { get; set; }
    public decimal TotalIncome { get; set; }
}
