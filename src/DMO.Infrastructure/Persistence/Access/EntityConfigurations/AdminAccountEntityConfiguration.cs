using DMO.Application.Accounts;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the single <c>admin_accounts</c> row (Migration 001).
/// </summary>
/// <remarks>
/// The single-ADMIN invariant is DB-enforced with two complementary constraints:
/// <c>admin_accounts_singleton_id_check</c> (every row must use the fixed
/// <see cref="AdminAccountId"/>) plus the primary key (that UUID only once). The PK alone is
/// not the singleton mechanism; the CHECK fixes the allowed value and the PK allows it once.
/// </remarks>
public sealed class AdminAccountEntityConfiguration : IEntityTypeConfiguration<AdminAccountEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AdminAccountEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("admin_accounts", table =>
        {
            table.HasCheckConstraint(
                "admin_accounts_singleton_id_check",
                $"admin_id = '{AdminAccountId.Value}'::uuid");
            table.HasCheckConstraint(
                "admin_accounts_auth_identity_id_required_check",
                "btrim(auth_identity_id) <> ''");
            table.HasCheckConstraint(
                "admin_accounts_email_required_check",
                "btrim(email) <> ''");
        });

        builder.HasKey(admin => admin.AdminId);
        builder.Property(admin => admin.AdminId)
            .HasColumnName("admin_id")
            .ValueGeneratedNever();

        builder.Property(admin => admin.AuthIdentityId)
            .HasColumnName("auth_identity_id")
            .IsRequired();
        builder.HasIndex(admin => admin.AuthIdentityId)
            .IsUnique()
            .HasDatabaseName("admin_accounts_auth_identity_id_key");

        builder.Property(admin => admin.DisplayName)
            .HasColumnName("display_name")
            .IsRequired();

        builder.Property(admin => admin.Email)
            .HasColumnName("email")
            .IsRequired();
        builder.HasIndex(admin => admin.Email)
            .IsUnique()
            .HasDatabaseName("admin_accounts_email_key");

        builder.Property(admin => admin.Active)
            .HasColumnName("active")
            .HasDefaultValue(true);
        builder.HasIndex(admin => admin.Active)
            .HasDatabaseName("admin_accounts_active_idx")
            .HasFilter("\"active\"");

        builder.Property(admin => admin.Version)
            .HasColumnName("version")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(admin => admin.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(admin => admin.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");
    }
}