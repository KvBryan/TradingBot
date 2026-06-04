using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace WebhookListener.Features.Webhooks;

public class CapitalComService
{
    private readonly HttpClient _httpClient;
    private readonly TradingBotDbContext _context;
    private readonly ILogger<CapitalComService> _logger;
    private readonly IHubContext<TradeHub> _hubContext;
    private readonly EconomicCalendarService _calendarService;
    private readonly TelegramNotificationService _telegramService;
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
        IHubContext<TradeHub> hubContext,
        EconomicCalendarService calendarService,
        TelegramNotificationService telegramService)
    {
        _httpClient = httpClient;
        _context = context;
        _logger = logger;
        _hubContext = hubContext;
        _calendarService = calendarService;
        _telegramService = telegramService;

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
        var symbol = alert.Symbol ?? throw new ArgumentException("El símbolo no puede ser nulo.");
        var action = alert.Action ?? throw new ArgumentException("La acción no puede ser nula.");
        var strategy = alert.Strategy ?? "Unknown";

        _logger.LogInformation("=== INICIO PROCESAMIENTO WEBHOOK ESTRATEGIA: {Strategy} ===", strategy);

        var isCloseAction = string.Equals(action, "CLOSE", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(action, "CANCEL", StringComparison.OrdinalIgnoreCase);

        if (isCloseAction)
        {
            _logger.LogInformation("Alerta de CIERRE/CANCELACIÓN recibida para {Symbol} - Estrategia: {Strategy}", symbol, strategy);

            // 1. Buscar la posición activa en nuestra DB
            var tradeToClose = await _context.Trades
                .Where(t => t.Ticker == symbol && t.Strategy == strategy && t.Status == "OPEN" && !t.IsDeleted)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (tradeToClose == null)
            {
                _logger.LogWarning("No se encontró ningún trade activo en PostgreSQL para {Symbol} y estrategia {Strategy} para cerrar.", symbol, strategy);
                return new WebhookResponse(false, "No se encontró ningún trade activo para cerrar.");
            }

            // 2. Cerrar en Capital.com si tenemos DealReference
            bool capitalClosed = false;
            if (!string.IsNullOrEmpty(tradeToClose.DealReference))
            {
                capitalClosed = await ClosePositionOnCapitalComAsync(tradeToClose.DealReference);
            }
            else
            {
                _logger.LogWarning("El trade activo no tiene DealReference almacenado. Cerrando únicamente en base de datos.");
            }

            // 3. Actualizar base de datos
            tradeToClose.Status = "CLOSED";
            tradeToClose.ProfitLoss = 0; // Opcional
            await _context.SaveChangesAsync();

            // 4. Notificar vía SignalR
            try
            {
                await _hubContext.Clients.All.SendAsync("TradeUpdated", tradeToClose);
                _logger.LogInformation("Trade de cierre transmitido vía SignalR.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al transmitir actualización de cierre por SignalR.");
            }

            // Notificación a Telegram
            var closeMsg = $"🔴 <b>ORDEN CERRADA</b>\n" +
                           $"<b>Par:</b> {symbol}\n" +
                           $"<b>Estrategia:</b> {strategy}\n" +
                           $"<b>Resultado:</b> {(capitalClosed ? "Cerrada en Broker y DB" : "Cerrada solo en DB (sin DealReference)")}";
            await _telegramService.SendNotificationAsync(closeMsg);

            return new WebhookResponse(
                Success: true,
                Message: capitalClosed 
                    ? $"Posición cerrada exitosamente en Capital.com y base de datos para {symbol}." 
                    : $"Posición cerrada en base de datos para {symbol} (no se pudo cerrar en Capital.com)."
            );
        }
        
        // Validar si ya hay un trade activo en PostgreSQL para evitar duplicar el riesgo
        var activeTrade = await _context.Trades
            .Where(t => t.Ticker == symbol && t.Strategy == strategy && t.Status == "OPEN" && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (activeTrade != null)
        {
            _logger.LogWarning("Se recibió una señal de APERTURA para {Symbol} con estrategia {Strategy}, pero ya existe una posición activa abierta en PostgreSQL.", symbol, strategy);
            
            var rejectMsg = $"⚠️ <b>ALERTA RECHAZADA (Doble Posición DB)</b>\n" +
                            $"<b>Par:</b> {symbol}\n" +
                            $"<b>Estrategia:</b> {strategy}\n" +
                            $"<b>Detalle:</b> Ya existe una posición abierta para este par en PostgreSQL.";
            await _telegramService.SendNotificationAsync(rejectMsg);

            return new WebhookResponse(false, $"Ya existe una posición abierta para {symbol} con esta estrategia. Orden ignorada para evitar sobreexposición.");
        }
        
        // 1. Obtener tokens de sesión válidos (reutilización)
        var (cst, securityToken) = await GetSessionTokensAsync();

        // --- NUEVA VALIDACIÓN: Calendario Económico Autónomo (Noticias Macro) ---
        var isNearNews = await _calendarService.IsNearHighImpactNewsAsync(symbol, DateTime.UtcNow);
        if (isNearNews)
        {
            _logger.LogWarning("Orden de APERTURA para {Symbol} rechazada en C# debido a proximidad con noticia macro de alto impacto (+/- 15 minutos).", symbol);
            
            var newsRejectMsg = $"⚠️ <b>ALERTA RECHAZADA (Filtro de Noticias)</b>\n" +
                                $"<b>Par:</b> {symbol}\n" +
                                $"<b>Estrategia:</b> {strategy}\n" +
                                $"<b>Detalle:</b> Proximidad con noticia macro de alto impacto (+/- 15 min).";
            await _telegramService.SendNotificationAsync(newsRejectMsg);

            return new WebhookResponse(false, "Orden rechazada por proximidad a noticia macro de alto impacto.");
        }

        // --- NUEVA VALIDACIÓN: Posición Abierta en Broker Real (Evitar desincronizaciones) ---
        var positionsReq = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/positions");
        positionsReq.Headers.Add("X-CAP-API-KEY", _apiKey);
        positionsReq.Headers.Add("CST", cst);
        positionsReq.Headers.Add("X-SECURITY-TOKEN", securityToken);

        var positionsResp = await _httpClient.SendAsync(positionsReq);
        if (!positionsResp.IsSuccessStatusCode)
        {
            throw new Exception($"Fallo al consultar posiciones abiertas en Capital.com. Status: {positionsResp.StatusCode}");
        }

        var posRawJson = await positionsResp.Content.ReadAsStringAsync();
        var serializerOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var activeCapitalPositions = System.Text.Json.JsonSerializer.Deserialize<CapitalPositionsResponse>(posRawJson, serializerOptions)?.Positions ?? new List<CapitalPositionItem>();

        var epic = ResolveEpic(symbol);
        var hasRealActivePosition = activeCapitalPositions.Any(p => 
            p.Position != null && 
            (string.Equals(p.Market?.Epic, epic, StringComparison.OrdinalIgnoreCase) || 
             string.Equals(p.Market?.Symbol, symbol, StringComparison.OrdinalIgnoreCase)));

        if (hasRealActivePosition)
        {
            _logger.LogWarning("Se recibió señal de APERTURA para {Symbol}, pero Capital.com ya tiene una posición abierta real en el broker.", symbol);
            
            var brokerRejectMsg = $"⚠️ <b>ALERTA RECHAZADA (Doble Posición Broker)</b>\n" +
                                  $"<b>Par:</b> {symbol}\n" +
                                  $"<b>Estrategia:</b> {strategy}\n" +
                                  $"<b>Detalle:</b> El broker Capital.com ya tiene una posición activa para este par.";
            await _telegramService.SendNotificationAsync(brokerRejectMsg);

            return new WebhookResponse(false, $"Orden ignorada. Ya existe una posición abierta en el broker real para {symbol}.");
        }

        // --- NUEVA VALIDACIÓN: Spread Dinámico en Tiempo Real ---
        var (bid, ask, marketStatus) = await GetMarketPricesAsync(cst, securityToken, epic);
        _logger.LogInformation("Consulta Precios en vivo para {Symbol}: Bid={Bid}, Ask={Ask}, Status={Status}", symbol, bid, ask, marketStatus);

        if (!marketStatus.Equals("TRADEABLE", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("El mercado para {Symbol} no está operable en este momento: {Status}", symbol, marketStatus);
            
            var mktRejectMsg = $"⚠️ <b>ALERTA RECHAZADA (Mercado no Operable)</b>\n" +
                               $"<b>Par:</b> {symbol}\n" +
                               $"<b>Estrategia:</b> {strategy}\n" +
                               $"<b>Detalle:</b> El mercado se encuentra en estado: {marketStatus}";
            await _telegramService.SendNotificationAsync(mktRejectMsg);

            return new WebhookResponse(false, $"Mercado no operable ({marketStatus}).");
        }

        var isJpyPair = symbol.Contains("JPY", StringComparison.OrdinalIgnoreCase);
        var pipSize = isJpyPair ? 0.01m : 0.0001m;
        var realSpread = Math.Abs(ask - bid);
        var realSpreadInPips = realSpread / pipSize;

        _logger.LogInformation("Spread en tiempo real para {Symbol}: {Spread} pips (Ask - Bid = {Ask} - {Bid})", symbol, realSpreadInPips.ToString("F2"), ask, bid);

        var maxAllowedSpread = 3.0m; // Límite de spread permitido (3 pips para Forex Majors)
        if (realSpreadInPips > maxAllowedSpread)
        {
            _logger.LogWarning("Orden de APERTURA para {Symbol} rechazada debido a spread real ensanchado: {RealSpread} pips (Máximo permitido: {MaxSpread} pips)", 
                symbol, realSpreadInPips.ToString("F2"), maxAllowedSpread);
            
            var spreadRejectMsg = $"⚠️ <b>ALERTA RECHAZADA (Spread Alto)</b>\n" +
                                  $"<b>Par:</b> {symbol}\n" +
                                  $"<b>Estrategia:</b> {strategy}\n" +
                                  $"<b>Detalle:</b> El spread real ({realSpreadInPips:F1} pips) supera el límite máximo permitido ({maxAllowedSpread} pips).";
            await _telegramService.SendNotificationAsync(spreadRejectMsg);

            return new WebhookResponse(false, $"Orden rechazada por spread real ensanchado ({realSpreadInPips:F1} pips).");
        }

        // 2. Obtener Balance
        var balance = await GetAccountBalanceAsync(cst, securityToken);

        // 3. Gestión de Riesgos y Regla del 1%
        var maxRisk = balance * 0.01m; // 1% del saldo líquido
        var slDistance = Math.Abs(alert.EntryPrice.GetValueOrDefault() - alert.StopLoss.GetValueOrDefault());

        if (slDistance == 0)
        {
            throw new ArgumentException("La distancia al Stop Loss no puede ser 0.");
        }

        // Definir tamaño de pip según convención Forex (4 decimales para estándar, 2 para JPY)
        isJpyPair = symbol.Contains("JPY", StringComparison.OrdinalIgnoreCase);
        pipSize = isJpyPair ? 0.01m : 0.0001m;
        var slDistanceInPips = slDistance / pipSize;

        // Tamaño de posición (Size) = Riesgo Máximo / (Distancia SL en Pips * Pip Value por Unidad)
        // Para Forex directo en cuenta USD, el Pip Value por unidad de contrato es el tamaño del Pip (pipSize).
        // Si el par tiene a USD como base (ej: USDJPY), el tamaño de posición en divisa base (USD) es:
        // Size = (Riesgo Máximo * EntryPrice) / slDistance
        // Para pares donde USD es el quote (ej: EURUSD), la relación simplificada es:
        // Size = Riesgo Máximo / slDistance
        
        decimal calculatedSize;
        if (symbol.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
        {
            calculatedSize = maxRisk / slDistance;
        }
        else if (symbol.StartsWith("USD", StringComparison.OrdinalIgnoreCase))
        {
            calculatedSize = maxRisk * alert.EntryPrice.GetValueOrDefault() / slDistance;
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
        epic = ResolveEpic(symbol);
        var positionReq = new PositionRequest(
            Epic: epic,
            Direction: action.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL",
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

        // Consultar confirmación para obtener el dealId real
        var dealId = await GetDealIdFromConfirmAsync(cst, securityToken, dealRef);
        var dealIdOrRef = dealId ?? dealRef; // Fallback a dealRef si no se confirma

        // 5. Guardar el registro en la base de datos PostgreSQL
        var trade = new Trade
        {
            Id = Guid.NewGuid(),
            Ticker = symbol,
            Strategy = strategy,
            Direction = action.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL",
            EntryPrice = alert.EntryPrice.GetValueOrDefault(),
            StopLoss = alert.StopLoss.GetValueOrDefault(),
            TakeProfit = alert.TakeProfit.GetValueOrDefault(),
            Size = calculatedSize,
            Status = "OPEN",
            ProfitLoss = null,
            CreatedAt = DateTime.UtcNow,
            DealReference = dealIdOrRef
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

        // Notificar a Telegram de la nueva orden ejecutada
        var successMsg = $"🟢 <b>NUEVA ORDEN EJECUTADA</b>\n" +
                         $"<b>Par:</b> {symbol}\n" +
                         $"<b>Dirección:</b> {trade.Direction}\n" +
                         $"<b>Lotes:</b> {calculatedSize}\n" +
                         $"<b>Entrada:</b> {trade.EntryPrice}\n" +
                         $"<b>SL:</b> {trade.StopLoss} | <b>TP:</b> {trade.TakeProfit}\n" +
                         $"<b>Estrategia:</b> {strategy}\n" +
                         $"<b>Referencia:</b> <code>{dealIdOrRef}</code>";
        await _telegramService.SendNotificationAsync(successMsg);

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

    /// <summary>
    /// Cierra una posición abierta en Capital.com usando su DealReference.
    /// </summary>
    public async Task<bool> ClosePositionOnCapitalComAsync(string dealReference)
    {
        try
        {
            var (cst, securityToken) = await GetSessionTokensAsync();

            string dealId = dealReference;

            // Si el identificador parece ser un dealReference de orden (empieza con "o_"), intentamos resolverlo a dealId
            if (dealReference.StartsWith("o_", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("El identificador {Identifier} parece ser un dealReference. Resolviendo a dealId...", dealReference);
                var resolvedId = await GetDealIdFromConfirmAsync(cst, securityToken, dealReference);
                if (!string.IsNullOrEmpty(resolvedId))
                {
                    dealId = resolvedId;
                    _logger.LogInformation("Resuelto a dealId desde confirms: {DealId}", dealId);
                }
                else
                {
                    _logger.LogWarning("No se pudo resolver el dealReference {Identifier} a un dealId mediante /confirms. Buscando en posiciones abiertas...", dealReference);
                    
                    // Como fallback, intentamos buscar en las posiciones abiertas
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/positions");
                    request.Headers.Add("X-CAP-API-KEY", _apiKey);
                    request.Headers.Add("CST", cst);
                    request.Headers.Add("X-SECURITY-TOKEN", securityToken);

                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var positionsResponse = await response.Content.ReadFromJsonAsync<CapitalPositionsResponse>();
                        var targetPosition = positionsResponse?.Positions?
                            .FirstOrDefault(p => string.Equals(p.Position?.DealReference, dealReference, StringComparison.OrdinalIgnoreCase));
                        
                        if (targetPosition?.Position != null)
                        {
                            dealId = targetPosition.Position.DealId ?? dealReference;
                            _logger.LogInformation("Resuelto a dealId desde posiciones abiertas: {DealId}", dealId);
                        }
                        else
                        {
                            _logger.LogWarning("No se encontró ninguna posición abierta en Capital.com con Identificador: {DealRef}", dealReference);
                            return false;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Fallo al listar posiciones en Capital.com. Status: {Status}", response.StatusCode);
                        return false;
                    }
                }
            }

            // Ejecutar el DELETE /positions/{dealId}
            _logger.LogInformation("Enviando DELETE /positions/{DealId} a Capital.com...", dealId);
            var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/positions/{dealId}");
            deleteRequest.Headers.Add("X-CAP-API-KEY", _apiKey);
            deleteRequest.Headers.Add("CST", cst);
            deleteRequest.Headers.Add("X-SECURITY-TOKEN", securityToken);

            var deleteResponse = await _httpClient.SendAsync(deleteRequest);
            if (!deleteResponse.IsSuccessStatusCode)
            {
                var errContent = await deleteResponse.Content.ReadAsStringAsync();
                _logger.LogError("Fallo al cerrar posición en Capital.com. Status: {Status}, Content: {Content}", deleteResponse.StatusCode, errContent);
                return false;
            }

            _logger.LogInformation("Posición {DealId} (Ref: {DealRef}) cerrada exitosamente en Capital.com.", dealId, dealReference);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción al cerrar posición en Capital.com para Identificador {DealRef}", dealReference);
            return false;
        }
    }

    /// <summary>
    /// Consulta el dealId de una posición mediante su dealReference con reintentos.
    /// </summary>
    private async Task<string?> GetDealIdFromConfirmAsync(string cst, string securityToken, string dealReference)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/confirms/{dealReference}");
                request.Headers.Add("X-CAP-API-KEY", _apiKey);
                request.Headers.Add("CST", cst);
                request.Headers.Add("X-SECURITY-TOKEN", securityToken);

                var response = await _httpClient.SendAsync(request);
                var rawJson = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Confirm API Response for {DealRef}: Status={StatusCode}, Body={Body}", 
                    dealReference, response.StatusCode, rawJson);

                if (response.IsSuccessStatusCode)
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var confirmData = System.Text.Json.JsonSerializer.Deserialize<CapitalConfirmResponse>(rawJson, options);
                    if (confirmData != null && confirmData.AffectedDeals != null && confirmData.AffectedDeals.Count > 0)
                    {
                        var dealId = confirmData.AffectedDeals[0].DealId;
                        _logger.LogInformation("Confirmado DealId {DealId} para DealReference {DealRef} tras {Attempts} intentos.", dealId, dealReference, i + 1);
                        return dealId;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al consultar confirmación para {DealRef} (Intento {Intento}/5)", dealReference, i + 1);
            }
            await Task.Delay(200); // Esperar 200ms antes del próximo reintento
        }
        return null;
    }

    /// <summary>
    /// Consulta Bid, Ask y estado del mercado en tiempo real de Capital.com
    /// </summary>
    private async Task<(decimal Bid, decimal Ask, string Status)> GetMarketPricesAsync(string cst, string securityToken, string epic)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/markets/{epic}");
        request.Headers.Add("X-CAP-API-KEY", _apiKey);
        request.Headers.Add("CST", cst);
        request.Headers.Add("X-SECURITY-TOKEN", securityToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Fallo al consultar precios de mercado para {epic}. Status: {response.StatusCode}");
        }

        var rawJson = await response.Content.ReadAsStringAsync();
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var marketData = System.Text.Json.JsonSerializer.Deserialize<CapitalMarketDetailsResponse>(rawJson, options);
        if (marketData?.Snapshot == null)
        {
            throw new Exception($"No se obtuvo snapshot de mercado para {epic}");
        }

        return (marketData.Snapshot.Bid, marketData.Snapshot.Offer, marketData.Snapshot.MarketStatus);
    }

    /// <summary>
    /// Sincroniza en tiempo real los estados de la base de datos de trades "OPEN" con las posiciones abiertas de Capital.com.
    /// También importa posiciones abiertas manualmente en Capital.com / TradingView.
    /// </summary>
    public async Task SyncOpenTradesAsync()
    {
        try
        {
            // 1. Obtener todos los trades con estado "OPEN" en PostgreSQL
            var openDbTrades = await _context.Trades
                .Where(t => t.Status == "OPEN" && !t.IsDeleted)
                .ToListAsync();

            // 2. Obtener las posiciones abiertas reales de Capital.com
            var (cst, securityToken) = await GetSessionTokensAsync();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/positions");
            request.Headers.Add("X-CAP-API-KEY", _apiKey);
            request.Headers.Add("CST", cst);
            request.Headers.Add("X-SECURITY-TOKEN", securityToken);

            var response = await _httpClient.SendAsync(request);
            var rawJson = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Positions API Response: {Body}", rawJson);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Omitiendo sincronización de estados: No se pudo consultar posiciones de Capital.com.");
                return;
            }

            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var positionsResponse = System.Text.Json.JsonSerializer.Deserialize<CapitalPositionsResponse>(rawJson, options);
            var activeCapitalPositions = positionsResponse?.Positions ?? new List<CapitalPositionItem>();

            // Crear un set de referencias abiertas en nuestra DB
            var dbOpenRefs = new HashSet<string>(
                openDbTrades
                    .Where(t => !string.IsNullOrEmpty(t.DealReference))
                    .Select(t => t.DealReference!), 
                StringComparer.OrdinalIgnoreCase
            );

            bool databaseUpdated = false;

            // 3. Detectar cuáles trades ya no están abiertos en Capital.com o actualizar sus parámetros si cambiaron
            foreach (var trade in openDbTrades)
            {
                // Buscar si la posición correspondiente sigue abierta en Capital.com
                var capitalPosition = activeCapitalPositions
                    .FirstOrDefault(p => p.Position != null && 
                                         (string.Equals(p.Position.DealId, trade.DealReference, StringComparison.OrdinalIgnoreCase) || 
                                          string.Equals(p.Position.DealReference, trade.DealReference, StringComparison.OrdinalIgnoreCase)));

                if (capitalPosition?.Position == null)
                {
                    // Si el trade no tiene DealReference o si su DealReference no está en la lista de Capital.com, se considera cerrado
                    _logger.LogInformation("Sincronización: El trade {TradeId} (Ticker: {Ticker}, Ref: {Ref}) ya no existe en Capital.com. Marcándolo como CLOSED.",
                        trade.Id, trade.Ticker, trade.DealReference);
                    trade.Status = "CLOSED";
                    trade.ProfitLoss = 0; // Opcional
                    databaseUpdated = true;
                    
                    // Notificar vía SignalR del cambio de estado
                    await _hubContext.Clients.All.SendAsync("TradeUpdated", trade);

                    // Notificar a Telegram del cierre
                    var closeMsg = $"⚠️ <b>CONCILIACIÓN: Posición Cerrada</b>\n" +
                                   $"La posición de <b>{trade.Ticker}</b> ({trade.Direction}) con referencia <code>{trade.DealReference}</code> ya no está activa en Capital.com.\n" +
                                   $"Se ha marcado localmente como <code>CLOSED</code>.";
                    await _telegramService.SendNotificationAsync(closeMsg);
                }
                else
                {
                    // La posición sigue abierta. Comprobar si se ha modificado SL, TP, volumen (Size), dirección o precio de entrada
                    var pos = capitalPosition.Position;
                    bool tradeChanged = false;

                    var newSL = pos.StopLevel ?? 0;
                    if (trade.StopLoss != newSL)
                    {
                        _logger.LogInformation("Sincronización: Modificación SL detectada para {Ticker} (Ref: {Ref}). Viejo: {Old}, Nuevo: {New}", trade.Ticker, trade.DealReference, trade.StopLoss, newSL);
                        trade.StopLoss = newSL;
                        tradeChanged = true;
                    }

                    var newTP = pos.ProfitLevel ?? 0;
                    if (trade.TakeProfit != newTP)
                    {
                        _logger.LogInformation("Sincronización: Modificación TP detectada para {Ticker} (Ref: {Ref}). Viejo: {Old}, Nuevo: {New}", trade.Ticker, trade.DealReference, trade.TakeProfit, newTP);
                        trade.TakeProfit = newTP;
                        tradeChanged = true;
                    }

                    var newSize = pos.Size ?? 0;
                    if (trade.Size != newSize)
                    {
                        _logger.LogInformation("Sincronización: Modificación Volumen (Size) detectada para {Ticker} (Ref: {Ref}). Viejo: {Old}, Nuevo: {New}", trade.Ticker, trade.DealReference, trade.Size, newSize);
                        trade.Size = newSize;
                        tradeChanged = true;
                    }

                    var newDirection = pos.Direction ?? "BUY";
                    if (!string.Equals(trade.Direction, newDirection, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Sincronización: Modificación Dirección detectada para {Ticker} (Ref: {Ref}). Viejo: {Old}, Nuevo: {New}", trade.Ticker, trade.DealReference, trade.Direction, newDirection);
                        trade.Direction = newDirection.ToUpperInvariant();
                        tradeChanged = true;
                    }

                    var newEntryPrice = pos.Level ?? 0;
                    if (trade.EntryPrice != newEntryPrice)
                    {
                        _logger.LogInformation("Sincronización: Modificación Precio de Entrada detectada para {Ticker} (Ref: {Ref}). Viejo: {Old}, Nuevo: {New}", trade.Ticker, trade.DealReference, trade.EntryPrice, newEntryPrice);
                        trade.EntryPrice = newEntryPrice;
                        tradeChanged = true;
                    }

                    var newUpl = pos.Upl;
                    if (trade.ProfitLoss != newUpl)
                    {
                        trade.ProfitLoss = newUpl;
                        tradeChanged = true;
                    }

                    if (tradeChanged)
                    {
                        databaseUpdated = true;
                        // Notificar vía SignalR del cambio en vivo
                        await _hubContext.Clients.All.SendAsync("TradeUpdated", trade);
                    }
                }
            }

            // 4. Importar posiciones abiertas en Capital.com que no estén registradas en nuestra base de datos
            foreach (var item in activeCapitalPositions)
            {
                if (item.Position == null || string.IsNullOrEmpty(item.Position.DealId)) continue;

                var dealId = item.Position.DealId;
                var dealRef = item.Position.DealReference;

                // Si no está registrado en la lista de trades abiertos locales
                bool alreadyTracked = dbOpenRefs.Contains(dealId) || (!string.IsNullOrEmpty(dealRef) && dbOpenRefs.Contains(dealRef));

                if (!alreadyTracked)
                {
                    // Comprobación de seguridad en la DB completa (por si está con otro estado o IsDeleted)
                    var existsInDb = await _context.Trades.AnyAsync(t => t.DealReference == dealId || (!string.IsNullOrEmpty(dealRef) && t.DealReference == dealRef));
                    if (existsInDb) continue;

                    _logger.LogInformation("Sincronización: Importando posición externa/manual activa de Capital.com: {Epic} | DealId: {DealId}",
                        item.Market?.Symbol ?? item.Market?.Epic ?? "UNKNOWN", dealId);

                    DateTime createdAt = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(item.Position.CreatedDate) && DateTime.TryParse(item.Position.CreatedDate, out var parsedDate))
                    {
                        createdAt = parsedDate.ToUniversalTime();
                    }

                    var newTrade = new Trade
                    {
                        Id = Guid.NewGuid(),
                        Ticker = item.Market?.Symbol ?? item.Market?.Epic ?? "UNKNOWN",
                        Strategy = "Manual / External",
                        Direction = item.Position.Direction ?? "BUY",
                        EntryPrice = item.Position.Level ?? 0,
                        StopLoss = item.Position.StopLevel ?? 0,
                        TakeProfit = item.Position.ProfitLevel ?? 0,
                        Size = item.Position.Size ?? 0,
                        Status = "OPEN",
                        ProfitLoss = item.Position.Upl,
                        CreatedAt = createdAt,
                        DealReference = dealId, // Guardamos el dealId directamente como referencia
                        IsDeleted = false
                    };

                    await _context.Trades.AddAsync(newTrade);
                    databaseUpdated = true;

                    // Emitir en tiempo real vía SignalR
                    await _hubContext.Clients.All.SendAsync("TradeUpdated", newTrade);

                    // Notificar a Telegram de la importación externa
                    var importMsg = $"ℹ️ <b>CONCILIACIÓN: Posición Externa Detectada</b>\n" +
                                    $"Se detectó e importó una posición externa/manual activa en Capital.com:\n" +
                                    $"<b>Par:</b> {newTrade.Ticker} | <b>Dirección:</b> {newTrade.Direction}\n" +
                                    $"<b>Lotes:</b> {newTrade.Size} | <b>Entrada:</b> {newTrade.EntryPrice}\n" +
                                    $"<b>Referencia:</b> <code>{newTrade.DealReference}</code>";
                    await _telegramService.SendNotificationAsync(importMsg);
                }
            }

            if (databaseUpdated)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Base de datos sincronizada con los estados de Capital.com.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al sincronizar los estados de los trades activos con Capital.com");
        }
    }
}
