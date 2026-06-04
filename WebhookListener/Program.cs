using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using WebhookListener;
using WebhookListener.Features.Webhooks;
using WebhookListener.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

// ==========================================
// 1. CARGA DE VARIABLES DE ENTORNO (.ENV)
// ==========================================
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) 
            continue;
            
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim();
            var val = parts[1].Trim();
            Environment.SetEnvironmentVariable(key, val);
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 2. CONFIGURACIÓN DE SERVICIOS (DI)
// ==========================================
builder.Services.AddEndpointsApiExplorer();

// Configuración de CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
    options.AddPolicy("CorsDashboard", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Registrar IHttpClientFactory para gestionar eficientemente conexiones salientes
builder.Services.AddHttpClient();

// Registrar el DbContext configurado con PostgreSQL
var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
builder.Services.AddDbContext<TradingBotDbContext>(options =>
    options.UseNpgsql(connectionString));

// Registrar CapitalComService como Scoped
builder.Services.AddSingleton<EconomicCalendarService>();
builder.Services.AddScoped<CapitalComService>();
builder.Services.AddHostedService<CapitalComSyncWorker>();

// Configuración de JWT
var jwtSecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
if (string.IsNullOrEmpty(jwtSecretKey))
{
    throw new InvalidOperationException("JWT_SECRET_KEY environment variable is not set.");
}
var keyBytes = Encoding.UTF8.GetBytes(jwtSecretKey);

builder.Services.AddSignalR();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ClockSkew = TimeSpan.Zero
    };

    // Permitir autenticación con JWT a través de SignalR query string
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    // Política para exigir JWT en endpoints del dashboard
    options.AddPolicy("DashboardPolicy", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
});


var app = builder.Build();

// --- INICIO: MIGRACIONES AUTOMÁTICAS PARA PRODUCCIÓN ---
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<TradingBotDbContext>();
        context.Database.Migrate(); // Esto creará las tablas automáticamente en la nube
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error al aplicar las migraciones automáticas.");
    }
}
// --- FIN: MIGRACIONES AUTOMÁTICAS ---

// ==========================================
// 3. MIDDLEWARE Y ROUTING
// ==========================================
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseCors("CorsDashboard");

app.UseAuthentication();
app.UseAuthorization();

// Map de endpoints de Vertical Slices
app.MapWebhookEndpoints();

// Mapear Hub de SignalR
app.MapHub<TradeHub>("/hubs/trades").RequireAuthorization("DashboardPolicy");

// REST endpoints para el Dashboard
app.MapGet("/api/v1/trades", async (TradingBotDbContext dbContext, CapitalComService capitalService) =>
{
    // Sincronizar en tiempo real estados activos con Capital.com antes de listar
    await capitalService.SyncOpenTradesAsync();

    var trades = await dbContext.Trades
        .Where(t => !t.IsDeleted)
        .OrderByDescending(t => t.CreatedAt)
        .ToListAsync();
    return Results.Ok(trades);
}).RequireAuthorization("DashboardPolicy");

app.MapGet("/api/v1/logs", async (TradingBotDbContext dbContext) =>
{
    var logs = await dbContext.SystemLogs.OrderByDescending(l => l.Timestamp).ToListAsync();
    return Results.Ok(logs);
}).RequireAuthorization("DashboardPolicy");

app.MapGet("/api/v1/balance", async (CapitalComService capitalService) =>
{
    try
    {
        var balance = await capitalService.GetCurrentBalanceAsync();
        var environment = Environment.GetEnvironmentVariable("CAPITAL_COM_ENVIRONMENT") ?? "DEMO";
        return Results.Ok(new { 
            balance, 
            isDemo = environment.Equals("DEMO", StringComparison.OrdinalIgnoreCase) 
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to fetch balance: {ex.Message}");
    }
}).RequireAuthorization("DashboardPolicy");

app.MapDelete("/api/v1/trades/{id:guid}", async (
    Guid id, 
    TradingBotDbContext dbContext, 
    CapitalComService capitalService, 
    IHubContext<TradeHub> hubContext) =>
{
    var trade = await dbContext.Trades.FindAsync(id);
    if (trade == null) return Results.NotFound();

    if (string.Equals(trade.Status, "OPEN", StringComparison.OrdinalIgnoreCase))
    {
        // 1. Es un trade abierto, por lo tanto "Liquidar" significa cerrarlo en Capital.com
        bool capitalClosed = false;
        if (!string.IsNullOrEmpty(trade.DealReference))
        {
            capitalClosed = await capitalService.ClosePositionOnCapitalComAsync(trade.DealReference);
        }

        // 2. Cambiar estado a LIQUIDATED sin borrar de la base de datos (mantiene IsDeleted = false)
        trade.Status = "LIQUIDATED";
        trade.ProfitLoss = 0; // Opcional o se puede calcular
        await dbContext.SaveChangesAsync();

        // 3. Notificar vía SignalR
        await hubContext.Clients.All.SendAsync("TradeUpdated", trade);

        return Results.Ok(new { 
            success = true, 
            message = capitalClosed 
                ? "Position liquidated successfully in Capital.com and database." 
                : "Position marked as Liquidated in database (Capital.com close was skipped or failed)." 
        });
    }
    else
    {
        // Es un trade ya cerrado o liquidado, el usuario quiere eliminarlo del historial (borrado lógico)
        trade.IsDeleted = true;
        await dbContext.SaveChangesAsync();

        // Notificar vía SignalR
        await hubContext.Clients.All.SendAsync("TradeUpdated", trade);

        return Results.Ok(new { success = true, message = "Trade removed from history successfully." });
    }
}).RequireAuthorization("DashboardPolicy");

// Endpoint de prueba de salud (Health Check)
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", time = DateTime.UtcNow })).AllowAnonymous();

// Endpoint de autenticación (Login)
app.MapPost("/api/v1/auth/login", (LoginDto loginDto) =>
{
    var adminEmail = Environment.GetEnvironmentVariable("DASHBOARD_ADMIN_EMAIL");
    var adminPassword = Environment.GetEnvironmentVariable("DASHBOARD_ADMIN_PASSWORD");

    if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
    {
        return Results.Problem("Dashboard admin credentials are not properly configured in the environment.");
    }

    if (string.Equals(loginDto.Email, adminEmail, StringComparison.Ordinal) &&
        string.Equals(loginDto.Password, adminPassword, StringComparison.Ordinal))
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Email, loginDto.Email),
                new Claim(ClaimTypes.Role, "Admin")
            }),
            Expires = DateTime.UtcNow.AddHours(2),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return Results.Ok(new 
        { 
            token = tokenString, 
            expires_at = DateTime.UtcNow.AddHours(2) 
        });
    }

    return Results.Unauthorized();
}).AllowAnonymous();

app.MapFallbackToFile("index.html");

app.Run();

// DTO para la petición de Login
public record LoginDto(string Email, string Password);
