using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>tools</c> table: the canonical Tool registry.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §3.1 (columns), §4.2 (identity tuple), §4.4 (checks) and §4.5
/// (indexes justified by contracted queries). The constraint names are the explicit database names
/// the repository maps PostgreSQL violations from.
/// </remarks>
public sealed class ToolEntityConfiguration : IEntityTypeConfiguration<ToolEntity>
{
    /// <summary>Database name of the canonical Tool identity-tuple unique index.</summary>
    public const string TypeReferenceLotUniqueConstraintName = "tools_type_reference_lot_key";

    /// <summary>Database name of the reference search index.</summary>
    public const string ReferenceIndexName = "tools_reference_idx";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ToolEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tools", table =>
        {
            table.HasCheckConstraint("tools_type_check", "tool_type IN ('CM','MF','BQ')");
            table.HasCheckConstraint("tools_reference_required_check", "btrim(reference) <> ''");
            table.HasCheckConstraint("tools_lot_required_check", "btrim(lot) <> ''");
            table.HasCheckConstraint("tools_processo_check", "processo IS NULL OR processo IN ('NNPB','PS')");
            table.HasCheckConstraint("tools_quantity_check", "quantity IS NULL OR quantity >= 0");
        });

        builder.HasKey(tool => tool.ToolId);
        builder.Property(tool => tool.ToolId)
            .HasColumnName("tool_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(tool => tool.ToolType)
            .HasColumnName("tool_type")
            .IsRequired();

        builder.Property(tool => tool.Reference)
            .HasColumnName("reference")
            .IsRequired();

        builder.Property(tool => tool.Lot)
            .HasColumnName("lot")
            .IsRequired();

        builder.Property(tool => tool.Processo)
            .HasColumnName("processo");

        builder.Property(tool => tool.Quantity)
            .HasColumnName("quantity");

        builder.Property(tool => tool.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(tool => tool.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        // The canonical Tool identity tuple: (tool_type, reference, lot). A different lot is a
        // different Tool. The leftmost prefix (tool_type, reference) also serves the dominant Tool
        // search when the type and reference are supplied.
        builder.HasIndex(tool => new { tool.ToolType, tool.Reference, tool.Lot })
            .IsUnique()
            .HasDatabaseName(TypeReferenceLotUniqueConstraintName);

        // Tool search prefilled from the Job On reference.
        builder.HasIndex(tool => tool.Reference)
            .HasDatabaseName(ReferenceIndexName);
    }
}
