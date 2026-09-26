using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>bq_contexts</c> table: the BQ production context of one Job On.
/// </summary>
/// <remarks>
/// Identical contracted shape to <see cref="CmContextEntityConfiguration"/>; the frozen Tool type is
/// fixed to <c>BQ</c> by the database check. The row is a Job On production context and never a
/// Boquilhas aggregate, movement or balance.
/// </remarks>
public sealed class BqContextEntityConfiguration : IEntityTypeConfiguration<BqContextEntity>
{
    /// <summary>Database name of the one-BQ-context-per-Job-On unique index.</summary>
    public const string JobOnUniqueConstraintName = "bq_contexts_jobon_key";

    /// <summary>Database name of the Job On foreign key.</summary>
    public const string JobOnForeignKeyConstraintName = "FK_bq_contexts_job_ons_jobon_id";

    /// <summary>Database name of the canonical Tool foreign key.</summary>
    public const string ToolForeignKeyConstraintName = "FK_bq_contexts_tools_tool_id";

    /// <summary>Database name of the FK-supporting canonical Tool index.</summary>
    public const string ToolIndexName = "IX_bq_contexts_tool_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BqContextEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("bq_contexts", table =>
        {
            table.HasCheckConstraint("bq_contexts_tool_type_check", "tool_type = 'BQ'");
            table.HasCheckConstraint("bq_contexts_tool_reference_required_check", "btrim(tool_reference) <> ''");
            table.HasCheckConstraint("bq_contexts_tool_lot_required_check", "btrim(tool_lot) <> ''");
        });

        builder.HasKey(context => context.BqId);
        builder.Property(context => context.BqId)
            .HasColumnName("bq_id")
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

        builder.HasIndex(context => context.JobOnId)
            .IsUnique()
            .HasDatabaseName(JobOnUniqueConstraintName);

        builder.HasIndex(context => context.ToolId)
            .HasDatabaseName(ToolIndexName);
    }
}
