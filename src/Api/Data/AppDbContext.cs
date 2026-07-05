using BillsBackend.Api.Domain;
using BillsBackend.Api.Identity;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.Api.Data;

/// <summary>
/// The Entity Framework Core database context for the budgeting backend.
/// </summary>
/// <remarks>
/// Column and table names are mapped to <c>snake_case</c> to match the PostgreSQL
/// (Neon) schema described in the project conventions.
/// <para>
/// The <paramref name="currentOwner"/> service scopes all domain entity queries to the
/// authenticated user via global query filters. Callers must set
/// <see cref="ICurrentOwner.Id"/> from the resolved <c>app_user.id</c> before issuing
/// any filtered query.
/// </para>
/// </remarks>
/// <param name="options">The options used to configure the context.</param>
/// <param name="currentOwner">The scoped current-owner context used by query filters.</param>
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentOwner currentOwner) : DbContext(options)
{
    /// <summary>Gets the set of application users.</summary>
    public DbSet<AppUser> Users => Set<AppUser>();

    /// <summary>
    /// Gets the pre-filtered set of budget categories for the current owner.
    /// Only active categories belonging to the authenticated owner are visible.
    /// </summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>
    /// Gets the pre-filtered set of persons for the current owner.
    /// Only active persons belonging to the authenticated owner are visible.
    /// </summary>
    public DbSet<Person> Persons => Set<Person>();

    /// <summary>
    /// Gets the pre-filtered set of income templates for the current owner.
    /// Only active incomes belonging to the authenticated owner are visible.
    /// </summary>
    public DbSet<Income> Incomes => Set<Income>();

    /// <summary>
    /// Gets the pre-filtered set of bill templates for the current owner.
    /// Only active bills belonging to the authenticated owner are visible.
    /// </summary>
    public DbSet<Bill> Bills => Set<Bill>();

    /// <summary>
    /// Gets the pre-filtered set of bill entries for the current owner.
    /// Only entries belonging to the authenticated owner are visible (no active filter — entries have no active flag).
    /// </summary>
    public DbSet<BillEntry> BillEntries => Set<BillEntry>();

    /// <summary>
    /// Gets the pre-filtered set of income entries for the current owner.
    /// Only entries belonging to the authenticated owner are visible (no active filter — entries have no active flag).
    /// </summary>
    public DbSet<IncomeEntry> IncomeEntries => Set<IncomeEntry>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_user");

            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(u => u.Name)
                .HasColumnName("name")
                .IsRequired();

            entity.Property(u => u.Email)
                .HasColumnName("email");

            entity.HasIndex(u => u.Email)
                .IsUnique();

            entity.Property(u => u.FirebaseUid)
                .HasColumnName("firebase_uid")
                .IsRequired();

            entity.Property(u => u.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.HasIndex(u => u.FirebaseUid)
                .IsUnique();
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("category");

            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(c => c.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();

            entity.Property(c => c.Name)
                .HasColumnName("name")
                .IsRequired();

            entity.Property(c => c.Active)
                .HasColumnName("active")
                .IsRequired();

            entity.Property(c => c.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.HasIndex(c => new { c.OwnerId, c.Name })
                .IsUnique();

            // Restricts all Category reads to the current owner's active rows.
            // currentOwner.Id is evaluated at query-execution time from the scoped service.
            entity.HasQueryFilter(c => c.Active && c.OwnerId == currentOwner.Id);
        });

        modelBuilder.Entity<Person>(entity =>
        {
            entity.ToTable("person");

            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(p => p.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();

            entity.Property(p => p.Name)
                .HasColumnName("name")
                .IsRequired();

            entity.Property(p => p.AppUserId)
                .HasColumnName("app_user_id");

            entity.Property(p => p.Active)
                .HasColumnName("active")
                .IsRequired();

            entity.Property(p => p.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            // Restricts all Person reads to the current owner's active rows.
            // currentOwner.Id is evaluated at query-execution time from the scoped service.
            entity.HasQueryFilter(p => p.Active && p.OwnerId == currentOwner.Id);
        });

        modelBuilder.Entity<Income>(entity =>
        {
            entity.ToTable("income");

            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(i => i.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();

            entity.Property(i => i.Name)
                .HasColumnName("name")
                .IsRequired();

            entity.Property(i => i.Kind)
                .HasColumnName("kind")
                .IsRequired()
                .HasConversion(
                    v => v == IncomeKind.Recurring ? "recurring" : "one_off",
                    v => v == "recurring" ? IncomeKind.Recurring : IncomeKind.OneOff);

            entity.Property(i => i.DefaultAmount)
                .HasColumnName("default_amount")
                .IsRequired();

            entity.Property(i => i.Active)
                .HasColumnName("active")
                .IsRequired();

            entity.Property(i => i.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            // Restricts all Income reads to the current owner's active rows.
            // currentOwner.Id is evaluated at query-execution time from the scoped service.
            entity.HasQueryFilter(i => i.Active && i.OwnerId == currentOwner.Id);
        });

        modelBuilder.Entity<Bill>(entity =>
        {
            entity.ToTable("bill");

            entity.HasKey(b => b.Id);
            entity.Property(b => b.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(b => b.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();

            entity.Property(b => b.Name)
                .HasColumnName("name")
                .IsRequired();

            entity.Property(b => b.CategoryId)
                .HasColumnName("category_id")
                .IsRequired();

            entity.Property(b => b.Kind)
                .HasColumnName("kind")
                .IsRequired()
                .HasConversion(
                    v => v == BillKind.Recurring ? "recurring" : "one_off",
                    v => v == "recurring" ? BillKind.Recurring : BillKind.OneOff);

            entity.Property(b => b.DefaultAmount)
                .HasColumnName("default_amount")
                .IsRequired();

            entity.Property(b => b.SplitRatio)
                .HasColumnName("split_ratio")
                .IsRequired();

            entity.Property(b => b.PersonId)
                .HasColumnName("person_id");

            entity.Property(b => b.Active)
                .HasColumnName("active")
                .IsRequired();

            entity.Property(b => b.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            // Restricts all Bill reads to the current owner's active rows.
            // currentOwner.Id is evaluated at query-execution time from the scoped service.
            entity.HasQueryFilter(b => b.Active && b.OwnerId == currentOwner.Id);
        });

        modelBuilder.Entity<BillEntry>(entity =>
        {
            entity.ToTable("bill_entry");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();

            entity.Property(e => e.BillId)
                .HasColumnName("bill_id")
                .IsRequired();

            entity.Property(e => e.RefYear)
                .HasColumnName("ref_year")
                .IsRequired();

            entity.Property(e => e.RefMonth)
                .HasColumnName("ref_month")
                .IsRequired();

            entity.Property(e => e.PlannedAmount)
                .HasColumnName("planned_amount")
                .IsRequired();

            entity.Property(e => e.ActualAmount)
                .HasColumnName("actual_amount");

            entity.Property(e => e.SplitRatioSnapshot)
                .HasColumnName("split_ratio_snapshot")
                .IsRequired();

            entity.Property(e => e.PersonId)
                .HasColumnName("person_id");

            entity.Property(e => e.Paid)
                .HasColumnName("paid")
                .IsRequired();

            entity.Property(e => e.PaidDate)
                .HasColumnName("paid_date");

            entity.Property(e => e.Received)
                .HasColumnName("received")
                .IsRequired();

            entity.Property(e => e.ReceivedDate)
                .HasColumnName("received_date");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.HasIndex(e => new { e.BillId, e.RefYear, e.RefMonth })
                .IsUnique();

            // Restricts all BillEntry reads to the current owner's rows.
            // No active filter — bill entries do not have an active flag.
            // currentOwner.Id is evaluated at query-execution time from the scoped service.
            entity.HasQueryFilter(e => e.OwnerId == currentOwner.Id);
        });

        modelBuilder.Entity<IncomeEntry>(entity =>
        {
            entity.ToTable("income_entry");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();

            entity.Property(e => e.IncomeId)
                .HasColumnName("income_id")
                .IsRequired();

            entity.Property(e => e.RefYear)
                .HasColumnName("ref_year")
                .IsRequired();

            entity.Property(e => e.RefMonth)
                .HasColumnName("ref_month")
                .IsRequired();

            entity.Property(e => e.PlannedAmount)
                .HasColumnName("planned_amount")
                .IsRequired();

            entity.Property(e => e.ActualAmount)
                .HasColumnName("actual_amount");

            entity.Property(e => e.Received)
                .HasColumnName("received")
                .IsRequired();

            entity.Property(e => e.ReceivedDate)
                .HasColumnName("received_date");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.HasIndex(e => new { e.IncomeId, e.RefYear, e.RefMonth })
                .IsUnique();

            // Restricts all IncomeEntry reads to the current owner's rows.
            // No active filter — income entries do not have an active flag.
            // currentOwner.Id is evaluated at query-execution time from the scoped service.
            entity.HasQueryFilter(e => e.OwnerId == currentOwner.Id);
        });
    }
}
