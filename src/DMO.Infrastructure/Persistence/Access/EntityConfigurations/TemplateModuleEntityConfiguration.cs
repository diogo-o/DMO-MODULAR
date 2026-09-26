using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>template_modules</c> table (Migration 002).
/// </summary>
public sealed class TemplateModuleEntityConfiguration : IEntityTypeConfiguration<TemplateModuleEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TemplateModuleEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("template_modules");

        builder.HasKey(module => new { module.TemplateId, module.ModuleId });

        builder.Property(module => module.TemplateId)
            .HasColumnName("template_id");

        builder.HasOne(module => module.Template)
            .WithMany()
            .HasForeignKey(module => module.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(module => module.ModuleId)
            .HasColumnName("module_id")
            .IsRequired();

        builder.Property(module => module.PresentationOrder)
            .HasColumnName("presentation_order");

        builder.HasIndex(module => new { module.TemplateId, module.PresentationOrder })
            .IsUnique()
            .HasDatabaseName("template_modules_template_order_key");
    }
}