using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>email_lists</c> table.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.6/§17. <c>btrim(name) &lt;&gt; ''</c> CHECK and the unique
/// <c>email_lists_name_key</c> (a named list must be unambiguously addressable — Q-NAME).
/// </remarks>
public sealed class EmailListEntityConfiguration : IEntityTypeConfiguration<EmailListEntity>
{
    /// <summary>Database name of the unique-list-name index.</summary>
    public const string NameUniqueConstraintName = "email_lists_name_key";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailListEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("email_lists", table =>
        {
            table.HasCheckConstraint("email_lists_name_required_check", "btrim(name) <> ''");
        });

        builder.HasKey(list => list.EmailListId);
        builder.Property(list => list.EmailListId)
            .HasColumnName("email_list_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(list => list.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(list => list.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(list => list.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(list => list.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasIndex(list => list.Name)
            .IsUnique()
            .HasDatabaseName(NameUniqueConstraintName);
    }
}