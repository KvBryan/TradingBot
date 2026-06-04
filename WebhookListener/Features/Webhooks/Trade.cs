namespace WebhookListener.Features.Webhooks;

// ReSharper disable PropertyCanBeMadeInitOnly.Global
public class Trade
{
    public Guid Id { get; init; }
    public string Ticker { get; init; } = string.Empty;
    public string Strategy { get; init; } = string.Empty;
    public string Direction { get; set; } = string.Empty; // BUY / SELL
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal Size { get; set; }
    public string Status { get; set; } = "OPEN"; // OPEN, WIN, LOSS
    public decimal? ProfitLoss { get; set; }
    public DateTime CreatedAt { get; init; }
    public string? DealReference { get; set; }
    public bool IsDeleted { get; set; }
}
