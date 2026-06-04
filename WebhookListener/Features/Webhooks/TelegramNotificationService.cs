using System.Text.Json;

namespace WebhookListener.Features.Webhooks;

public class TelegramNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramNotificationService> _logger;
    private readonly string? _botToken;
    private readonly string? _chatId;

    public TelegramNotificationService(HttpClient httpClient, ILogger<TelegramNotificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        _chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
    }

    public async Task SendNotificationAsync(string message)
    {
        if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(_chatId))
        {
            _logger.LogWarning("TelegramNotification: Notificación omitida (Token o ChatID no configurado): {Message}", message);
            return;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            var payload = new
            {
                chat_id = _chatId,
                text = message,
                parse_mode = "HTML"
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, jsonContent);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Notificación enviada a Telegram exitosamente.");
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Error al enviar notificación a Telegram. Status: {Status}, Response: {Response}", response.StatusCode, responseContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo excepcional al enviar notificación a Telegram.");
        }
    }
}
