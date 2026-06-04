namespace WebhookListener.Features.Webhooks;

public class CapitalComSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CapitalComSyncWorker> _logger;

    public CapitalComSyncWorker(IServiceProvider serviceProvider, ILogger<CapitalComSyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CapitalComSyncWorker iniciado. Sincronización programada cada 5 segundos.");

        // Esperar unos segundos para permitir que la aplicación arranque completamente
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var syncService = scope.ServiceProvider.GetRequiredService<CapitalComService>();
                    await syncService.SyncOpenTradesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ocurrido durante la sincronización de CapitalComSyncWorker.");
            }

            // Pausa de 5 segundos
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
