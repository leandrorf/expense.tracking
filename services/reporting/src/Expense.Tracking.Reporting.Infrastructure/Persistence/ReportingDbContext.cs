using Expense.Tracking.Reporting.Domain.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

/// <summary>EVT-2: banco proprio do reporting (<c>expense_reporting</c>) com a projecao e o checkpoint.</summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<ProjectedTransaction> Transactions => Set<ProjectedTransaction>();

    public DbSet<ProjectionCheckpoint> Checkpoints => Set<ProjectionCheckpoint>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateOnly>().HaveConversion<DateOnlyConverter>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectedTransaction>(b =>
        {
            b.ToTable("projected_transactions");
            b.HasKey(t => t.TransactionId);
            b.Property(t => t.TransactionId).HasColumnName("transaction_id").ValueGeneratedNever();
            b.Property(t => t.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
            b.Property(t => t.AccountId).HasColumnName("account_id").IsRequired();
            b.Property(t => t.AccountScope).HasColumnName("account_scope").HasMaxLength(16);
            b.Property(t => t.TransactionType).HasColumnName("transaction_type").HasMaxLength(16).IsRequired();
            b.Property(t => t.PaymentMethod).HasColumnName("payment_method").HasMaxLength(20).IsRequired();
            b.Property(t => t.Category).HasColumnName("category").HasMaxLength(60).IsRequired();
            b.Property(t => t.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
            b.Property(t => t.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            b.Property(t => t.OccurredOn).HasColumnName("occurred_on").IsRequired();
            b.Property(t => t.LastEventSequence).HasColumnName("last_event_sequence").IsRequired();
            // Consulta do resumo (EVT-3): por dono, opcionalmente por conta e periodo.
            b.HasIndex(t => new { t.OwnerUserId, t.OccurredOn }).HasDatabaseName("ix_projected_transactions_owner_date");
        });

        modelBuilder.Entity<ProjectionCheckpoint>(b =>
        {
            b.ToTable("projection_checkpoints");
            b.HasKey(c => c.Feed);
            b.Property(c => c.Feed).HasColumnName("feed").HasMaxLength(64).ValueGeneratedNever();
            b.Property(c => c.LastSequence).HasColumnName("last_sequence").IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Posicao do consumidor num feed. Zerar <see cref="LastSequence"/> reconstroi a projecao.</summary>
public sealed class ProjectionCheckpoint
{
    public const string TransactionsFeed = "transactions-events";

    public string Feed { get; set; } = TransactionsFeed;

    public long LastSequence { get; set; }
}

/// <summary>Converte DateOnly &lt;-&gt; DateTime (portavel entre provedores).</summary>
public sealed class DateOnlyConverter : ValueConverter<DateOnly, DateTime>
{
    public DateOnlyConverter()
        : base(d => d.ToDateTime(TimeOnly.MinValue), dt => DateOnly.FromDateTime(dt))
    {
    }
}
