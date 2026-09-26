using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>repairers</c> table: the canonical repairer register.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.3/§17. <c>btrim(name) &lt;&gt; ''</c> CHECK; no unique name
/// (Q-REP), no status column, no additional business field (§10.1/§10.3).
/// </remarks>
public sealed class RepairerEntityConfiguration : IEntityTypeConfiguration<RepairerEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RepairerEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("repairers", table =>
        {
            table.HasCheckConstraint("repairers_name_required_check", "btrim(name) <> ''");
        });

        builder.HasKey(repairer => repairer.RepairerId);
        builder.Property(repairer => repairer.RepairerId)
            .HasColumnName("repairer_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(repairer => repairer.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(repairer => repairer.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(repairer => repairer.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(repairer => repairer.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");
    }
}