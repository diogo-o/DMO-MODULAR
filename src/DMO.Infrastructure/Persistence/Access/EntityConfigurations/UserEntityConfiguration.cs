using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>users</c> table (Migration 001).
/// </summary>
public sealed class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    /// <summary>Database name of the unique provider-subject index.</summary>
    public const string AuthIdentityIdUniqueConstraintName = "users_auth_identity_id_key";

    /// <summary>Database name of the unique company-number index.</summary>
    public const string CompanyNumberUniqueConstraintName = "users_company_number_key";

    /// <summary>Database name of the Template foreign key (EF-conventional).</summary>
    public const string TemplateForeignKeyConstraintName = "users_template_id_fkey";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint("users_auth_identity_id_required_check", "btrim(auth_identity_id) <> ''");
            table.HasCheckConstraint("users_company_number_required_check", "btrim(company_number) <> ''");
            table.HasCheckConstraint("users_email_required_check", "btrim(email) <> ''");
        });

        builder.HasKey(user => user.UserId);
        builder.Property(user => user.UserId)
            .HasColumnName("user_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(user => user.AuthIdentityId)
            .HasColumnName("auth_identity_id")
            .IsRequired();
        builder.HasIndex(user => user.AuthIdentityId)
            .IsUnique()
            .HasDatabaseName(AuthIdentityIdUniqueConstraintName);

        builder.Property(user => user.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(user => user.CompanyNumber)
            .HasColumnName("company_number")
            .IsRequired();
        builder.HasIndex(user => user.CompanyNumber)
            .IsUnique()
            .HasDatabaseName(CompanyNumberUniqueConstraintName);

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .IsRequired();

        builder.Property(user => user.Role)
            .HasColumnName("role");

        builder.Property(user => user.Active)
            .HasColumnName("active")
            .HasDefaultValue(true);
        builder.HasIndex(user => user.Active)
            .HasDatabaseName("users_active_idx")
            .HasFilter("\"active\"");

        builder.Property(user => user.TemplateId)
            .HasColumnName("template_id");

        builder.HasOne(user => user.Template)
            .WithMany()
            .HasForeignKey(user => user.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(user => user.Version)
            .HasColumnName("version")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(user => user.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(user => user.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");
    }
}