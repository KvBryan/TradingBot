using Microsoft.EntityFrameworkCore;
using WebhookListener.Features.Webhooks;

namespace WebhookListener;

public class TradingBotDbContext : DbContext
{
    public TradingBotDbContext()
    {
    }

    public TradingBotDbContext(DbContextOptions<TradingBotDbContext> options)
        : base(options)
    {
    }

    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
        {
            return;
        }

        // Cargar el archivo .env si estamos en tiempo de diseño / desarrollo local
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) 
                    continue;
                    
                var parts = line.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }
                
                var key = parts[0].Trim();
                var val = parts[1].Trim();
                Environment.SetEnvironmentVariable(key, val);
            }
        }

        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("CONNECTION_STRING environment variable is not set.");
        }

        optionsBuilder.UseNpgsql(connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Trade>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Ticker).IsRequired().HasMaxLength(50);
            entity.Property(t => t.Strategy).IsRequired().HasMaxLength(100);
            entity.Property(t => t.Direction).IsRequired().HasMaxLength(10);
            entity.Property(t => t.Status).IsRequired().HasMaxLength(20);

            // Precisiones adecuadas para Forex y tamaños de posición
            entity.Property(t => t.EntryPrice).HasPrecision(18, 6);
            entity.Property(t => t.StopLoss).HasPrecision(18, 6);
            entity.Property(t => t.TakeProfit).HasPrecision(18, 6);
            entity.Property(t => t.Size).HasPrecision(18, 4);
            entity.Property(t => t.ProfitLoss).HasPrecision(18, 6);
            entity.Property(t => t.IsDeleted).HasDefaultValue(false);
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Id).ValueGeneratedOnAdd();
            entity.Property(l => l.Timestamp).IsRequired();
            entity.Property(l => l.LogLevel).IsRequired().HasMaxLength(50);
            entity.Property(l => l.Source).IsRequired().HasMaxLength(100);
            entity.Property(l => l.Message).IsRequired().HasMaxLength(1000);
            entity.Property(l => l.StackTrace).IsRequired(false).HasMaxLength(4000);
        });
    }
}
