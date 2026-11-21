using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

/// <summary>
/// EVT-2: banco proprio do reporting (<c>expense_reporting</c>) com a projecao e o checkpoint.
/// BDG-2: tambem guarda os orcamentos do usuario (ADR-0002).
/// </summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<ProjectedTransaction> Transactions => Set<ProjectedTransaction>();

    public DbSet<ProjectionCheckpoint> Checkpoints => Set<ProjectionCheckpoint>();

    public DbSet<Budget> Budgets => Set<Budget>();

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
            b.Property(t => t.Description).HasColumnName("description").HasMaxLength(200);
            b.Property(t => t.CreditCardId).HasColumnName("credit_card_id");
            b.Property(t => t.Installments).HasColumnName("installments").IsRequired().HasDefaultValue(1);
            // Consulta do resumo (EVT-3): por dono, opcionalmente por conta e periodo.
            b.HasIndex(t => new { t.OwnerUserId, t.OccurredOn }).HasDatabaseName("ix_projected_transactions_owner_date");
            // Fatura (INV-4): compras de um cartao do dono.
            b.HasIndex(t => new { t.OwnerUserId, t.CreditCardId }).HasDatabaseName("ix_projected_transactions_owner_card");
        });

        // BDG-2/BDG-3 (ADR-0002): dado escrito pelo usuario, fora da projecao.
        modelBuilder.Entity<Budget>(b =>
        {
            b.ToTable("budgets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
            b.Property(x => x.Category).HasColumnName("category").HasMaxLength(Budget.CategoryMaxLength).IsRequired();
            b.Property(x => x.CategoryKey).HasColumnName("category_key").HasMaxLength(Budget.CategoryMaxLength).IsRequired();
            b.Property(x => x.OpenCategoryKey).HasColumnName("open_category_key").HasMaxLength(Budget.CategoryMaxLength);
            b.Property(x => x.Scope).HasColumnName("scope").HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            b.Property(x => x.StartMonth).HasColumnName("start_month").IsRequired();
            b.Property(x => x.WarningThreshold).HasColumnName("warning_threshold")
                .HasDefaultValue(Budget.DefaultWarningThreshold).IsRequired();
            b.Property(x => x.ClosedFromMonth).HasColumnName("closed_from_month");
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Ignore(x => x.Limits);
            b.Ignore(x => x.LatestAmount);
            // Um so orcamento ABERTO por dono + escopo + categoria (encerrados tem chave nula).
            b.HasIndex(x => new { x.OwnerUserId, x.Scope, x.OpenCategoryKey })
                .IsUnique()
                .HasDatabaseName("ux_budgets_owner_scope_open_category");
            b.HasIndex(x => new { x.OwnerUserId, x.Scope, x.CategoryKey })
                .HasDatabaseName("ix_budgets_owner_scope_category");

            b.OwnsMany<BudgetLimit>("_limits", l =>
            {
                l.ToTable("budget_limits");
                l.WithOwner().HasForeignKey("BudgetId");
                l.Property<Guid>("BudgetId").HasColumnName("budget_id");
                l.Property(x => x.EffectiveFrom).HasColumnName("effective_from").IsRequired();
                l.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
                l.HasKey("BudgetId", nameof(BudgetLimit.EffectiveFrom));
            });
            b.Navigation("_limits").AutoInclude();
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
