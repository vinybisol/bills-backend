using BillsBackend.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.Api.Data;

/// <summary>
/// The Entity Framework Core database context for the budgeting backend.
/// </summary>
/// <remarks>
/// Column and table names are mapped to <c>snake_case</c> to match the PostgreSQL
/// (Neon) schema described in the project conventions.
/// </remarks>
/// <param name="options">The options used to configure the context.</param>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Gets the set of application users.
    /// </summary>
    /// <value>The <see cref="AppUser"/> entities tracked by the context.</value>
    public DbSet<AppUser> Users => Set<AppUser>();

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
    }
}
