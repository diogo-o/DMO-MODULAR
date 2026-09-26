using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>templates</c> table (Migration 001).
/// </summary>
public sealed class TemplateEntityConfiguration : IEntityTypeConfiguration<TemplateEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TemplateEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("templates");

        builder.HasKey(template => template.TemplateId);
        builder.Property(template => template.TemplateId)
            .HasColumnName("template_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(template => template.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(template => template.LandingDestinationId)
            .HasColumnName("landing_destination_id");

        builder.Property(template => template.Version)
            .HasColumnName("version")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(template => template.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(template => template.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");
    }
}