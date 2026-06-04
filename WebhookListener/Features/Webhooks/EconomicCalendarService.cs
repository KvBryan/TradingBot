using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebhookListener.Features.Webhooks;

public class EconomicCalendarService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EconomicCalendarService> _logger;
    private List<EconomicEvent> _events = new();
    private DateTime _lastFetchTime = DateTime.MinValue;
    private readonly string _cacheFilePath = "news_calendar.json";

    public EconomicCalendarService(HttpClient httpClient, ILogger<EconomicCalendarService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        // 1. Cargar caché de disco a memoria RAM solo si la lista interna está vacía (por ejemplo, al iniciar)
        if (_events.Count == 0)
        {
            await LoadCachedEventsAsync();
        }
        
        var provider = Environment.GetEnvironmentVariable("ECONOMIC_CALENDAR_PROVIDER");
        var apiKey = Environment.GetEnvironmentVariable("ECONOMIC_CALENDAR_API_KEY");

        if (!string.IsNullOrEmpty(provider) && !string.IsNullOrEmpty(apiKey))
        {
            // 2. Comprobar si el archivo local no existe o fue modificado hace más de 24 horas
            bool fileExists = File.Exists(_cacheFilePath);
            bool isOld = !fileExists || (DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFilePath) > TimeSpan.FromDays(1));

            if (isOld)
            {
                try
                {
                    await FetchFromProviderAsync(provider, apiKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al sincronizar el calendario económico externo. Usando base de datos caché local.");
                }
            }
        }
    }

    private async Task LoadCachedEventsAsync()
    {
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                var json = await File.ReadAllTextAsync(_cacheFilePath);
                _events = JsonSerializer.Deserialize<List<EconomicEvent>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                _logger.LogInformation("Cargados {Count} eventos macroeconómicos de la caché local.", _events.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar la caché local de noticias en {Path}", _cacheFilePath);
        }
    }

    private async Task SaveCachedEventsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_events, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_cacheFilePath, json);
            _logger.LogInformation("Guardados {Count} eventos en la caché local.", _events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar eventos de noticias en la caché local.");
        }
    }

    private async Task FetchFromProviderAsync(string provider, string apiKey)
    {
        _logger.LogInformation("Sincronizando calendario económico usando proveedor: {Provider}", provider);

        if (provider.Equals("FMP", StringComparison.OrdinalIgnoreCase))
        {
            var fromDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var toDate = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
            var url = $"https://financialmodelingprep.com/api/v3/economic_calendar?from={fromDate}&to={toDate}&apikey={apiKey}";

            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var rawJson = await response.Content.ReadAsStringAsync();
                var fmpEvents = JsonSerializer.Deserialize<List<FmpEconomicEvent>>(rawJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                
                _events = fmpEvents
                    .Where(e => e.Impact != null && e.Impact.Equals("high", StringComparison.OrdinalIgnoreCase))
                    .Select(e => new EconomicEvent
                    {
                        Date = ParseDateTime(e.Date),
                        Currency = MapCountryToCurrency(e.Country),
                        Importance = "HIGH",
                        Event = e.Event ?? "Unknown Macro Event"
                    }).ToList();

                _lastFetchTime = DateTime.UtcNow;
                await SaveCachedEventsAsync();
            }
            else
            {
                _logger.LogWarning("FMP Calendar API retornó código de estado: {Status}", response.StatusCode);
            }
        }
        else
        {
            _logger.LogWarning("Proveedor de calendario económico no soportado: {Provider}", provider);
        }
    }

    private string MapCountryToCurrency(string? country)
    {
        if (string.IsNullOrEmpty(country)) return "USD";
        return country.ToUpperInvariant() switch
        {
            "US" => "USD",
            "EU" => "EUR",
            "GB" => "GBP",
            "JP" => "JPY",
            "CA" => "CAD",
            "AU" => "AUD",
            "CH" => "CHF",
            _ => country
        };
    }

    private DateTime ParseDateTime(string? dateStr)
    {
        if (DateTime.TryParse(dateStr, out var date))
        {
            return date.ToUniversalTime();
        }
        return DateTime.UtcNow;
    }

    public async Task<bool> IsNearHighImpactNewsAsync(string symbol, DateTime checkTimeUtc)
    {
        await InitializeAsync();

        if (_events.Count == 0)
        {
            return false;
        }

        var cleanSymbol = symbol.Replace("/", "").Replace("-", "").Trim().ToUpperInvariant();
        if (cleanSymbol.Length < 6)
        {
            return false;
        }

        var baseCurrency = cleanSymbol.Substring(0, 3);
        var quoteCurrency = cleanSymbol.Substring(3, 3);

        var newsWindow = TimeSpan.FromMinutes(15);

        foreach (var ev in _events)
        {
            if (ev.Importance.Equals("HIGH", StringComparison.OrdinalIgnoreCase))
            {
                var evCurrency = ev.Currency.ToUpperInvariant();
                if (evCurrency == baseCurrency || evCurrency == quoteCurrency)
                {
                    var timeDiff = (checkTimeUtc - ev.Date).Duration();
                    if (timeDiff <= newsWindow)
                    {
                        _logger.LogWarning("Bloqueo de Noticias: Webhook ignorado para {Symbol} debido a evento de alto impacto: {Event} ({Currency}) programado para {Date} (Diferencia: {Diff}m)", 
                            symbol, ev.Event, ev.Currency, ev.Date, Math.Round(timeDiff.TotalMinutes, 1));
                        return true;
                    }
                }
            }
        }

        return false;
    }
}

public class EconomicEvent
{
    public DateTime Date { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Importance { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
}

public record FmpEconomicEvent(
    [property: JsonPropertyName("date")] string? Date = null,
    [property: JsonPropertyName("country")] string? Country = null,
    [property: JsonPropertyName("event")] string? Event = null,
    [property: JsonPropertyName("impact")] string? Impact = null
);
