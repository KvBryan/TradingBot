using System.Text.Json;

namespace WebhookListener.Features.Webhooks;

public static class ReceiveWebhookEndpoint
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> ProcessedTradeIds = new();

    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhook", async (
            HttpContext context,
            CapitalComService capitalService,
            ILogger<ReceiveWebhookEndpointLog> logger) =>
        {
            // 1. Leer el cuerpo de la petición como string en bruto (evita problemas de Content-Type)
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(body))
            {
                logger.LogWarning("Petición de Webhook vacía recibida.");
                return Results.BadRequest(new WebhookResponse(false, "El cuerpo de la alerta no puede ser nulo."));
            }

            // 2. Intentar deserializar el JSON manualmente
            WebhookRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<WebhookRequest>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Error al deserializar el cuerpo del webhook. Body recibido: {Body}", body);
                return Results.BadRequest(new WebhookResponse(false, $"Error al deserializar JSON: {ex.Message}. Asegúrate de enviar un formato JSON válido sin caracteres especiales o barras invertidas incorrectas."));
            }

            if (request == null)
            {
                logger.LogWarning("Petición de Webhook vacía tras deserialización.");
                return Results.BadRequest(new WebhookResponse(false, "El cuerpo de la alerta JSON no puede ser nulo."));
            }

            // Validar Idempotencia (Evitar duplicidades por reintentos de red)
            if (!string.IsNullOrEmpty(request.TradeId))
            {
                if (ProcessedTradeIds.ContainsKey(request.TradeId))
                {
                    logger.LogWarning("Webhook con TradeId duplicado recibido y rechazado: {TradeId}", request.TradeId);
                    return Results.Ok(new WebhookResponse(true, "Operación duplicada ya procesada anteriormente."));
                }
                ProcessedTradeIds.TryAdd(request.TradeId, true);
            }

            // 3. Normalizar acción
            var action = request.Action?.ToUpper().Trim();

            // 4. Validación diferenciada: Si es apertura, exigir precios estrictos de SL y TP
            var isCloseAction = string.Equals(action, "CLOSE", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(action, "CANCEL", StringComparison.OrdinalIgnoreCase);

            if (!isCloseAction)
            {
                if (string.IsNullOrWhiteSpace(request.Symbol) ||
                    request.EntryPrice.GetValueOrDefault() <= 0 ||
                    request.StopLoss.GetValueOrDefault() <= 0 ||
                    request.TakeProfit.GetValueOrDefault() <= 0)
                {
                    logger.LogWarning("Estructura de APERTURA (BUY/SELL) inválida: {@Request}", request);
                    return Results.BadRequest(new WebhookResponse(false, "Para abrir posiciones (BUY/SELL) debes proveer action, symbol, entry_price, stop_loss y take_profit mayores a cero."));
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Symbol))
                {
                    logger.LogWarning("Estructura de CIERRE inválida (falta symbol): {@Request}", request);
                    return Results.BadRequest(new WebhookResponse(false, "Para cerrar posiciones debes proveer al menos el campo symbol."));
                }
            }

            logger.LogInformation("Procesando Webhook de Forex -> Ticker: {Symbol} | Acción: {Action} | Entrada: {EntryPrice} | SL: {StopLoss} | TP: {TakeProfit} | Estrategia: {Strategy}",
                request.Symbol, action, request.EntryPrice, request.StopLoss, request.TakeProfit, request.Strategy);

            // 5. Ejecutar orden
            var response = await capitalService.ExecuteWebhookOrderAsync(request);
            return Results.Ok(response);
        })
        .WithName("ReceiveTradingViewWebhook");
    }
}

internal class ReceiveWebhookEndpointLog { }

