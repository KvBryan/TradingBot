using System.Net;
using System.Text.Json;
using WebhookListener.Features.Webhooks;

namespace WebhookListener.Middleware;

public class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IServiceScopeFactory scopeFactory)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ocurrió un error inesperado al procesar la solicitud.");
            await LogToDatabaseAsync(ex);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task LogToDatabaseAsync(Exception exception)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();

            var source = exception.TargetSite?.DeclaringType?.Name ?? exception.Source ?? "Unknown";

            var log = new SystemLog
            {
                Timestamp = DateTime.UtcNow,
                LogLevel = "ERROR",
                Source = source,
                Message = exception.Message,
                StackTrace = exception.StackTrace
            };

            dbContext.SystemLogs.Add(log);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception dbEx)
        {
            logger.LogError(dbEx, "No se pudo guardar el SystemLog en la base de datos.");
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var payload = new
        {
            success = false,
            message = "Internal Server Error. La ejecución de la alerta falló.",
            error = exception.Message
        };

        var json = JsonSerializer.Serialize(payload);
        return context.Response.WriteAsync(json);
    }
}
