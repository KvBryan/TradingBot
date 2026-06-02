using Microsoft.EntityFrameworkCore;
using WebhookListener;
using WebhookListener.Features.Webhooks;
using WebhookListener.Middleware;

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

// Registrar IHttpClientFactory para gestionar eficientemente conexiones salientes
builder.Services.AddHttpClient();

// Registrar el DbContext configurado con PostgreSQL
var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
builder.Services.AddDbContext<TradingBotDbContext>(options =>
    options.UseNpgsql(connectionString));

// Registrar CapitalComService como Scoped
builder.Services.AddScoped<CapitalComService>();

var app = builder.Build();

// ==========================================
// 3. MIDDLEWARE Y ROUTING
// ==========================================
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Map de endpoints de Vertical Slices
app.MapWebhookEndpoints();

// Endpoint de prueba de salud (Health Check)
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", time = DateTime.UtcNow }));

app.Run();
