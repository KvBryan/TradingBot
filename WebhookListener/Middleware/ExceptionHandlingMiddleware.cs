using System.Net;
using System.Text.Json;

namespace WebhookListener.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
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
            await HandleExceptionAsync(context, ex);
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
