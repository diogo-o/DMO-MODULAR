using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>email_list_recipients</c> table.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.7/§17. <c>btrim(address) &lt;&gt; ''</c> CHECK; the unique
/// <c>email_list_recipients_list_address_key</c> (one address once per list; the same address may
/// appear in several lists) is also the FK + "recipients of list" query index. No <c>version</c>:
/// the parent list's version protects the set.
/// </remarks>
public sealed class EmailListRecipientEntityConfiguration : IEntityTypeConfiguration<EmailListRecipientEntity>
{
    /// <summary>Database name of the (email_list_id, address) unique key.</summary>
    public const string ListAddressUniqueConstraintName = "email_list_recipients_list_address_key";

    /// <summary>Database name of the owning-list foreign key.</summary>
    public const string ListForeignKeyConstraintName = "FK_email_list_recipients_email_lists_email_list_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailListRecipientEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("email_list_recipients", table =>
        {
            table.HasCheckConstraint("email_list_recipients_address_required_check", "btrim(address) <> ''");
        });

        builder.HasKey(recipient => recipient.EmailListRecipientId);
        builder.Property(recipient => recipient.EmailListRecipientId)
            .HasColumnName("email_list_recipient_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(recipient => recipient.EmailListId)
            .HasColumnName("email_list_id")
            .IsRequired();

        builder.Property(recipient => recipient.Address)
            .HasColumnName("address")
            .IsRequired();

        builder.Property(recipient => recipient.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<EmailListEntity>()
            .WithMany()
            .HasForeignKey(recipient => recipient.EmailListId)
            .HasConstraintName(ListForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(recipient => new { recipient.EmailListId, recipient.Address })
            .IsUnique()
            .HasDatabaseName(ListAddressUniqueConstraintName);
    }
}