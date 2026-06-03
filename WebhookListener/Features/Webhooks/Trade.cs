namespace WebhookListener.Features.Webhooks;

// ReSharper disable PropertyCanBeMadeInitOnly.Global
public class Trade
{
    public Guid Id { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public string Strategy { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty; // BUY / SELL
    public decimal EntryPrice { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }
    public decimal Size { get; init; }
    public string Status { get; set; } = "OPEN"; // OPEN, WIN, LOSS
    public decimal? ProfitLoss { get; set; }
    public DateTime CreatedAt { get; init; }
    public bool IsDeleted { get; set; }
}
