using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>pdf_directory_settings</c> table: the single-row base-directory
/// setting.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.5/§17. The <c>singleton</c> CHECK + UNIQUE is the physical
/// single-row mechanism; <c>btrim(base_directory) &lt;&gt; ''</c> CHECK. An absent row is the
/// explicit <c>not-configured</c> state.
/// </remarks>
public sealed class PdfDirectorySettingsEntityConfiguration : IEntityTypeConfiguration<PdfDirectorySettingsEntity>
{
    /// <summary>Database name of the single-row unique key.</summary>
    public const string SingletonUniqueConstraintName = "pdf_directory_settings_singleton_key";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PdfDirectorySettingsEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("pdf_directory_settings", table =>
        {
            table.HasCheckConstraint("pdf_directory_settings_singleton_check", "singleton");
            table.HasCheckConstraint(
                "pdf_directory_settings_directory_required_check",
                "btrim(base_directory) <> ''");
        });

        builder.HasKey(settings => settings.PdfDirectorySettingId);
        builder.Property(settings => settings.PdfDirectorySettingId)
            .HasColumnName("pdf_directory_setting_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(settings => settings.Singleton)
            .HasColumnName("singleton")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(settings => settings.BaseDirectory)
            .HasColumnName("base_directory")
            .IsRequired();

        builder.Property(settings => settings.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(settings => settings.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasIndex(settings => settings.Singleton)
            .IsUnique()
            .HasDatabaseName(SingletonUniqueConstraintName);
    }
}