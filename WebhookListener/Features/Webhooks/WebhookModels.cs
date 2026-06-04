// ReSharper disable NotAccessedPositionalProperty.Global
// ReSharper disable ClassNeverInstantiated.Global

using System.Text.Json.Serialization;

namespace WebhookListener.Features.Webhooks;

// --- DTO de Entrada para Alertas de TradingView ---
public record WebhookRequest(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("entry_price")] decimal? EntryPrice = null,
    [property: JsonPropertyName("stop_loss")] decimal? StopLoss = null,
    [property: JsonPropertyName("take_profit")] decimal? TakeProfit = null,
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("lot_size")] decimal? LotSize = null,
    [property: JsonPropertyName("magic_number")] int? MagicNumber = null,
    [property: JsonPropertyName("trade_id")] string? TradeId = null
);

// --- DTO de Respuesta de la API Webhook ---
public record WebhookResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("dealReference")] string? DealReference = null,
    [property: JsonPropertyName("calculatedSize")] decimal? CalculatedSize = null
);

// --- DTOs de Sesión de Capital.com ---
public record SessionRequest(
    [property: JsonPropertyName("identifier")] string Identifier,
    [property: JsonPropertyName("password")] string Password
);

// --- DTOs de Consulta de Balance en Capital.com ---
public record AccountsResponse(
    [property: JsonPropertyName("accounts")] List<Account> Accounts
);

public record Account(
    [property: JsonPropertyName("accountId")] string AccountId,
    [property: JsonPropertyName("accountName")] string AccountName,
    [property: JsonPropertyName("balance")] AccountBalance Balance
);

public record AccountBalance(
    [property: JsonPropertyName("balance")] decimal Balance,
    [property: JsonPropertyName("equity")] decimal Equity,
    [property: JsonPropertyName("available")] decimal Available
);

// --- DTOs de Órdenes de Posición en Capital.com ---
public record PositionRequest(
    [property: JsonPropertyName("epic")] string Epic,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("stopLevel")] decimal? StopLevel = null,
    [property: JsonPropertyName("profitLevel")] decimal? ProfitLevel = null,
    [property: JsonPropertyName("guaranteedStop")] bool GuaranteedStop = false
);

public record PositionResponse(
    [property: JsonPropertyName("dealReference")] string DealReference
);

// --- DTOs para Obtener Posiciones Abiertas de Capital.com ---
public record CapitalPositionsResponse(
    [property: JsonPropertyName("positions")] List<CapitalPositionItem>? Positions
);

public record CapitalPositionItem(
    [property: JsonPropertyName("position")] CapitalPositionDetails? Position,
    [property: JsonPropertyName("market")] CapitalMarketDetails? Market
);

public record CapitalPositionDetails(
    [property: JsonPropertyName("dealId")] string? DealId,
    [property: JsonPropertyName("dealReference")] string? DealReference,
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonPropertyName("size")] decimal? Size,
    [property: JsonPropertyName("level")] decimal? Level,
    [property: JsonPropertyName("createdDate")] string? CreatedDate,
    [property: JsonPropertyName("stopLevel")] decimal? StopLevel,
    [property: JsonPropertyName("profitLevel")] decimal? ProfitLevel,
    [property: JsonPropertyName("upl")] decimal? Upl
);

public record CapitalMarketDetails(
    [property: JsonPropertyName("epic")] string? Epic,
    [property: JsonPropertyName("symbol")] string? Symbol,
    [property: JsonPropertyName("instrumentName")] string? InstrumentName
);

// --- DTOs para Confirmación de Órdenes de Capital.com ---
public record CapitalConfirmResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("affectedDeals")] List<CapitalAffectedDeal> AffectedDeals
);

public record CapitalAffectedDeal(
    [property: JsonPropertyName("dealId")] string DealId,
    [property: JsonPropertyName("status")] string Status
);
