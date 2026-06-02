namespace WebhookListener.Features.Webhooks;

public static class ReceiveWebhookEndpoint
{
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhook", async (
            WebhookRequest? request,
            CapitalComService capitalService,
            ILogger<ReceiveWebhookEndpointLog> logger) =>
        {
            // 1. Validar que la alerta contenga información
            if (request == null)
            {
                logger.LogWarning("Petición de Webhook vacía o nula recibida.");
                return Results.BadRequest(new WebhookResponse(false, "El cuerpo de la alerta JSON no puede ser nulo."));
            }

            // 2. Validación de Campos Críticos
            if (string.IsNullOrWhiteSpace(request.Action) ||
                string.IsNullOrWhiteSpace(request.Symbol) ||
                request.EntryPrice <= 0 ||
                request.StopLoss <= 0 ||
                request.TakeProfit <= 0)
            {
                logger.LogWarning("Estructura JSON inválida o valores menores/iguales a cero: {@Request}", request);
                return Results.BadRequest(new WebhookResponse(false, "Estructura JSON incorrecta. Asegúrate de proveer action, symbol, entry_price, stop_loss y take_profit correctos."));
            }

            logger.LogInformation("Alerta Recibida -> Ticker: {Symbol} | Acción: {Action} | Entrada: {EntryPrice} | SL: {StopLoss} | TP: {TakeProfit} | Estrategia: {Strategy}",
                request.Symbol, request.Action, request.EntryPrice, request.StopLoss, request.TakeProfit, request.Strategy);

            // 3. Ejecutar orden
            var response = await capitalService.ExecuteWebhookOrderAsync(request);
            return Results.Ok(response);
        })
        .WithName("ReceiveTradingViewWebhook");
    }
}

internal class ReceiveWebhookEndpointLog { }
