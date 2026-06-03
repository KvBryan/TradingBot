using Microsoft.AspNetCore.SignalR;

namespace WebhookListener.Features.Webhooks;

public class CapitalComService
{
    private readonly HttpClient _httpClient;
    private readonly TradingBotDbContext _context;
    private readonly ILogger<CapitalComService> _logger;
    private readonly IHubContext<TradeHub> _hubContext;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _identifier;
    private readonly string _password;

    // --- Variables de Sesión Persistente (Caché en Memoria) ---
    private static string? _cst;
    private static string? _securityToken;
    private static DateTime _tokenExpiration = DateTime.MinValue;
    private static readonly SemaphoreSlim _sessionLock = new(1, 1);

    // --- Mapeo de Tickers de TradingView a EPICs Oficiales ---
    private static readonly Dictionary<string, string> TickerToEpicMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "EURUSD", "EURUSD" },
        { "GBPUSD", "GBPUSD" },
        { "USDJPY", "USDJPY" },
        { "AUDUSD", "AUDUSD" },
        { "USDCAD", "USDCAD" },
        { "USDCHF", "USDCHF" }
    };

    public CapitalComService(
        HttpClient httpClient,
        TradingBotDbContext context,
        ILogger<CapitalComService> logger,
        IHubContext<TradeHub> hubContext)
    {
        _httpClient = httpClient;
        _context = context;
        _logger = logger;
        _hubContext = hubContext;

        // Leer variables de entorno cargadas en la aplicación
        var environment = Environment.GetEnvironmentVariable("CAPITAL_COM_ENVIRONMENT") ?? "DEMO";
        _apiKey = Environment.GetEnvironmentVariable("CAPITAL_COM_API_KEY") 
            ?? throw new InvalidOperationException("CAPITAL_COM_API_KEY environment variable is missing.");
        _identifier = Environment.GetEnvironmentVariable("CAPITAL_COM_IDENTIFIER") 
            ?? throw new InvalidOperationException("CAPITAL_COM_IDENTIFIER environment variable is missing.");
        _password = Environment.GetEnvironmentVariable("CAPITAL_COM_PASSWORD") 
            ?? throw new InvalidOperationException("CAPITAL_COM_PASSWORD environment variable is missing.");

        _baseUrl = environment.Equals("LIVE", StringComparison.OrdinalIgnoreCase)
            ? "https://api-capital.backend-capital.com/api/v1"
            : "https://demo-api-capital.backend-capital.com/api/v1";

        _logger.LogInformation("CapitalComService inicializado para el entorno: {Environment} (URL: {BaseUrl})", environment, _baseUrl);
    }

    /// <summary>
    /// Resuelve el EPIC correspondiente para un símbolo de TradingView
    /// </summary>
    private string ResolveEpic(string ticker)
    {
        // Limpiar posibles prefijos (ej: "FX:EURUSD") o guiones
        var cleanTicker = ticker;
        if (ticker.Contains(':'))
        {
            cleanTicker = ticker.Split(':').Last();
        }
        cleanTicker = cleanTicker.Replace("/", "").Replace("-", "").Trim();

        if (TickerToEpicMap.TryGetValue(cleanTicker, out var epic))
        {
            return epic;
        }

        _logger.LogWarning("El símbolo '{Ticker}' (Limpiado: '{CleanTicker}') no tiene mapeo explícito. Usando el nombre del ticker directamente como EPIC.", ticker, cleanTicker);
        return cleanTicker;
    }

    /// <summary>
    /// Obtiene los tokens de sesión válidos mediante Login en /session o caché
    /// </summary>
    private async Task<(string Cst, string SecurityToken)> GetSessionTokensAsync()
    {
        // Verificar si la sesión guardada en caché sigue activa (colocamos margen de seguridad de 9 minutos)
        if (!string.IsNullOrEmpty(_cst) && !string.IsNullOrEmpty(_securityToken) && DateTime.UtcNow < _tokenExpiration)
        {
            _logger.LogInformation("Reutilizando tokens de sesión activos de Capital.com (Expira en: {RemainingTime}s)", (_tokenExpiration - DateTime.UtcNow).TotalSeconds);
            return (_cst, _securityToken);
        }

        // Semáforo para evitar llamadas de login recurrentes/concurrentes
        await _sessionLock.WaitAsync();
        try
        {
            // Doble comprobación tras cruzar el semáforo
            if (!string.IsNullOrEmpty(_cst) && !string.IsNullOrEmpty(_securityToken) && DateTime.UtcNow < _tokenExpiration)
            {
                return (_cst, _securityToken);
            }

            _logger.LogInformation("Iniciando nueva sesión en Capital.com para el identificador: {Identifier}", _identifier);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/session");
            request.Headers.Add("X-CAP-API-KEY", _apiKey);
            request.Content = JsonContent.Create(new SessionRequest(_identifier, _password));

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Fallo de login en Capital.com. Status: {Status}, Content: {Content}", response.StatusCode, errContent);
                throw new Exception($"Failed to authenticate session: {response.StatusCode}. Details: {errContent}");
            }

            if (!response.Headers.TryGetValues("CST", out var cstValues) ||
                !response.Headers.TryGetValues("X-SECURITY-TOKEN", out var tokenValues))
            {
                throw new Exception("La respuesta de autenticación de Capital.com no contenía los encabezados CST o X-SECURITY-TOKEN.");
            }

            _cst = cstValues.FirstOrDefault();
            _securityToken = tokenValues.FirstOrDefault();
            
            // Las sesiones de Capital.com suelen expirar en varias horas, guardamos por 9 minutos
            _tokenExpiration = DateTime.UtcNow.AddMinutes(9);
            _logger.LogInformation("Login exitoso en Capital.com. Tokens CST y X-SECURITY-TOKEN almacenados en memoria caché.");
            
            return (_cst!, _securityToken!);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>
    /// Consulta el balance líquido de la cuenta
    /// </summary>
    private async Task<decimal> GetAccountBalanceAsync(string cst, string securityToken)
    {
        _logger.LogInformation("Consultando balance de cuentas en Capital.com...");

        var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/accounts");
        request.Headers.Add("X-CAP-API-KEY", _apiKey);
        request.Headers.Add("CST", cst);
        request.Headers.Add("X-SECURITY-TOKEN", securityToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Error al consultar balance de cuentas. Status: {Status}, Content: {Content}", response.StatusCode, errContent);
            throw new Exception($"Failed to fetch accounts balance: {response.StatusCode}");
        }

        var accountsData = await response.Content.ReadFromJsonAsync<AccountsResponse>();
        var primaryAccount = accountsData?.Accounts.FirstOrDefault();
        
        if (primaryAccount == null)
        {
            throw new Exception("No se encontraron cuentas de trading en Capital.com.");
        }

        _logger.LogInformation("Cuenta seleccionada: {AccountName} ({AccountId}). Balance Líquido: {Balance} {Currency}", 
            primaryAccount.AccountName, primaryAccount.AccountId, primaryAccount.Balance.Balance, "USD");

        return primaryAccount.Balance.Balance;
    }

    /// <summary>
    /// Ejecuta la lógica de trading recibida en el webhook: Login -> Balance -> Lot Sizing -> OCO Order
    /// </summary>
    public async Task<WebhookResponse> ExecuteWebhookOrderAsync(WebhookRequest alert)
    {
        _logger.LogInformation("=== INICIO PROCESAMIENTO WEBHOOK ESTRATEGIA: {Strategy} ===", alert.Strategy);
        
        // 1. Obtener tokens de sesión válidos (reutilización)
        var (cst, securityToken) = await GetSessionTokensAsync();

        // 2. Obtener Balance
        var balance = await GetAccountBalanceAsync(cst, securityToken);

        // 3. Gestión de Riesgos y Regla del 1%
        var maxRisk = balance * 0.01m; // 1% del saldo líquido
        var slDistance = Math.Abs(alert.EntryPrice - alert.StopLoss);

        if (slDistance == 0)
        {
            throw new ArgumentException("La distancia al Stop Loss no puede ser 0.");
        }

        // Definir tamaño de pip según convención Forex (4 decimales para estándar, 2 para JPY)
        var isJpyPair = alert.Symbol.Contains("JPY", StringComparison.OrdinalIgnoreCase);
        var pipSize = isJpyPair ? 0.01m : 0.0001m;
        var slDistanceInPips = slDistance / pipSize;

        // Tamaño de posición (Size) = Riesgo Máximo / (Distancia SL en Pips * Pip Value por Unidad)
        // Para Forex directo en cuenta USD, el Pip Value por unidad de contrato es el tamaño del Pip (pipSize).
        // Si el par tiene a USD como base (ej: USDJPY), el tamaño de posición en divisa base (USD) es:
        // Size = (Riesgo Máximo * EntryPrice) / slDistance
        // Para pares donde USD es el quote (ej: EURUSD), la relación simplificada es:
        // Size = Riesgo Máximo / slDistance
        
        decimal calculatedSize;
        if (alert.Symbol.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
        {
            calculatedSize = maxRisk / slDistance;
        }
        else if (alert.Symbol.StartsWith("USD", StringComparison.OrdinalIgnoreCase))
        {
            calculatedSize = maxRisk * alert.EntryPrice / slDistance;
        }
        else
        {
            // Para cruces (ej: EURGBP) usamos la fórmula general por defecto
            calculatedSize = maxRisk / slDistance;
        }

        // Redondear el lote a 2 decimales para compatibilidad de volumen CFD
        calculatedSize = Math.Round(calculatedSize, 2);

        if (calculatedSize <= 0)
        {
            throw new Exception($"El tamaño del lote calculado es inválido ({calculatedSize}). Incrementa el balance o ajusta la distancia de Stop Loss.");
        }

        _logger.LogInformation("Gestión de Riesgo: Riesgo Max (1%): {Risk} USD | Distancia SL: {Dist} pips | Tamaño Lote Calculado: {Size}", 
            maxRisk.ToString("F2"), slDistanceInPips.ToString("F1"), calculatedSize);

        // 4. Enviar Orden Bracket OCO
        var epic = ResolveEpic(alert.Symbol);
        var positionReq = new PositionRequest(
            Epic: epic,
            Direction: alert.Action.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL",
            Size: calculatedSize,
            StopLevel: alert.StopLoss,
            ProfitLevel: alert.TakeProfit
        );

        _logger.LogInformation("Enviando orden Bracket OCO a mercado en Capital.com para EPIC: {Epic} | Dirección: {Direction} | Stop Loss: {SL} | Take Profit: {TP}...", 
            epic, positionReq.Direction, alert.StopLoss, alert.TakeProfit);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/positions");
        request.Headers.Add("X-CAP-API-KEY", _apiKey);
        request.Headers.Add("CST", cst);
        request.Headers.Add("X-SECURITY-TOKEN", securityToken);
        request.Content = JsonContent.Create(positionReq);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Fallo al colocar posición en Capital.com (Posible error de margen/volumen). Status: {Status}, Content: {Content}", response.StatusCode, errContent);
            throw new Exception($"Capital.com Positions API rejected: {response.StatusCode}. Details: {errContent}");
        }

        var posResponse = await response.Content.ReadFromJsonAsync<PositionResponse>();
        var dealRef = posResponse?.DealReference ?? "UnknownReference";

        _logger.LogInformation("=== ORDEN EN PILOTO AUTOMÁTICO CONFIRMADA. Ref: {DealRef} ===", dealRef);

        // 5. Guardar el registro en la base de datos PostgreSQL
        var trade = new Trade
        {
            Id = Guid.NewGuid(),
            Ticker = alert.Symbol,
            Strategy = alert.Strategy,
            Direction = alert.Action.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL",
            EntryPrice = alert.EntryPrice,
            StopLoss = alert.StopLoss,
            TakeProfit = alert.TakeProfit,
            Size = calculatedSize,
            Status = "OPEN",
            ProfitLoss = null,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Trades.AddAsync(trade);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Trade guardado exitosamente en PostgreSQL con ID: {TradeId}", trade.Id);

        // Notificar en tiempo real vía SignalR
        try
        {
            await _hubContext.Clients.All.SendAsync("TradeUpdated", trade);
            _logger.LogInformation("Trade broadcasted via SignalR to all clients.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al transmitir actualización de trade a través de SignalR Hub.");
        }

        return new WebhookResponse(
            Success: true,
            Message: $"Posición Bracket abierta exitosamente en Capital.com para {epic}.",
            DealReference: dealRef,
            CalculatedSize: calculatedSize
        );
    }

    /// <summary>
    /// Método público para obtener el balance actual de la cuenta conectada.
    /// </summary>
    public async Task<decimal> GetCurrentBalanceAsync()
    {
        var (cst, securityToken) = await GetSessionTokensAsync();
        return await GetAccountBalanceAsync(cst, securityToken);
    }
}
