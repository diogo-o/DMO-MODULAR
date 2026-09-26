using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>cm_contexts</c> table: the CM production context of one Job On.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §3.4 (columns), §4.2 (one context per Job On), §4.3 (RESTRICT FKs),
/// §4.4 (per-context type check) and §4.5 (the FK-supporting <c>tool_id</c> index). The table
/// carries no <c>version</c> column: a context is only written inside a Job On mutation
/// transaction, and the Job On version protects the aggregate.
/// </remarks>
public sealed class CmContextEntityConfiguration : IEntityTypeConfiguration<CmContextEntity>
{
    /// <summary>Database name of the one-CM-context-per-Job-On unique index.</summary>
    public const string JobOnUniqueConstraintName = "cm_contexts_jobon_key";

    /// <summary>Database name of the Job On foreign key.</summary>
    public const string JobOnForeignKeyConstraintName = "FK_cm_contexts_job_ons_jobon_id";

    /// <summary>Database name of the canonical Tool foreign key.</summary>
    public const string ToolForeignKeyConstraintName = "FK_cm_contexts_tools_tool_id";

    /// <summary>Database name of the FK-supporting canonical Tool index.</summary>
    public const string ToolIndexName = "IX_cm_contexts_tool_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CmContextEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cm_contexts", table =>
        {
            // The per-context type check makes a context type and its frozen Tool type unable to
            // disagree in persisted data.
            table.HasCheckConstraint("cm_contexts_tool_type_check", "tool_type = 'CM'");
            table.HasCheckConstraint("cm_contexts_tool_reference_required_check", "btrim(tool_reference) <> ''");
            table.HasCheckConstraint("cm_contexts_tool_lot_required_check", "btrim(tool_lot) <> ''");
        });

        builder.HasKey(context => context.CmId);
        builder.Property(context => context.CmId)
            .HasColumnName("cm_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(context => context.JobOnId)
            .HasColumnName("jobon_id")
            .IsRequired();

        builder.Property(context => context.ToolId)
            .HasColumnName("tool_id")
            .IsRequired();

        builder.Property(context => context.ToolType)
            .HasColumnName("tool_type")
            .IsRequired();

        builder.Property(context => context.ToolReference)
            .HasColumnName("tool_reference")
            .IsRequired();

        builder.Property(context => context.ToolLot)
            .HasColumnName("tool_lot")
            .IsRequired();

        builder.Property(context => context.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(context => context.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<JobOnEntity>()
            .WithMany()
            .HasForeignKey(context => context.JobOnId)
            .HasConstraintName(JobOnForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ToolEntity>()
            .WithMany()
            .HasForeignKey(context => context.ToolId)
            .HasConstraintName(ToolForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one CM context per Job On; this unique index also covers the jobon_id foreign key,
        // so no separate FK index is contracted.
        builder.HasIndex(context => context.JobOnId)
            .IsUnique()
            .HasDatabaseName(JobOnUniqueConstraintName);

        // "Which occurrences used this Tool" is a query through this index, never a stored array.
        builder.HasIndex(context => context.ToolId)
            .HasDatabaseName(ToolIndexName);
    }
}
