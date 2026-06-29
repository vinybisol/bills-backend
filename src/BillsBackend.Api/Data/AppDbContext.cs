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
    }
}
