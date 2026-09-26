using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>email_templates</c> table.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.8/§17; P2-T08 email slice (group routing). Unique
/// <c>email_templates_name_key</c>; non-blank name/subject/body CHECKs; the <c>document_type</c>
/// CHECK allows <c>NULL</c> (generic) or one of the three Beta output families (Q-DOCTYPE). The
/// <c>machine_group</c> CHECK allows <c>NULL</c> or exactly <c>B</c>/<c>C</c>; the optional
/// <c>email_list_id</c> FK is RESTRICT (a list referenced by a template can never be deleted
/// silently) — together they are the slice's group routing, stored on the template row itself
/// (no generic rule architecture).
/// </remarks>
public sealed class EmailTemplateEntityConfiguration : IEntityTypeConfiguration<EmailTemplateEntity>
{
    /// <summary>Database name of the unique-template-name index.</summary>
    public const string NameUniqueConstraintName = "email_templates_name_key";

    /// <summary>Database name of the FK index on <c>email_list_id</c>.</summary>
    public const string EmailListIndexName = "IX_email_templates_email_list_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailTemplateEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("email_templates", table =>
        {
            table.HasCheckConstraint("email_templates_name_required_check", "btrim(name) <> ''");
            table.HasCheckConstraint("email_templates_subject_required_check", "btrim(subject) <> ''");
            table.HasCheckConstraint("email_templates_body_required_check", "btrim(body) <> ''");
            table.HasCheckConstraint(
                "email_templates_document_type_check",
                "document_type IS NULL OR document_type IN ('peso','pegamentos','resumo')");
            table.HasCheckConstraint(
                "email_templates_machine_group_check",
                "machine_group IS NULL OR machine_group IN ('B','C')");
        });

        builder.HasKey(template => template.EmailTemplateId);
        builder.Property(template => template.EmailTemplateId)
            .HasColumnName("email_template_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(template => template.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(template => template.Subject)
            .HasColumnName("subject")
            .IsRequired();

        builder.Property(template => template.Body)
            .HasColumnName("body")
            .IsRequired();

        builder.Property(template => template.DocumentType)
            .HasColumnName("document_type");

        builder.Property(template => template.MachineGroup)
            .HasColumnName("machine_group");

        builder.Property(template => template.EmailListId)
            .HasColumnName("email_list_id");

        builder.HasOne<EmailListEntity>()
            .WithMany()
            .HasForeignKey(template => template.EmailListId)
            .HasConstraintName("email_templates_email_list_id_fkey")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(template => template.EmailListId)
            .HasDatabaseName(EmailListIndexName);

        builder.Property(template => template.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(template => template.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(template => template.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasIndex(template => template.Name)
            .IsUnique()
            .HasDatabaseName(NameUniqueConstraintName);
    }
}