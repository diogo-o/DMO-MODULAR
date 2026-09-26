using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>mf_contexts</c> table: the MF production context of one Job On.
/// </summary>
/// <remarks>
/// Identical contracted shape to <see cref="CmContextEntityConfiguration"/>; the frozen Tool type is
/// fixed to <c>MF</c> by the database check, so an MF context can never record a CM or BQ frozen
/// type.
/// </remarks>
public sealed class MfContextEntityConfiguration : IEntityTypeConfiguration<MfContextEntity>
{
    /// <summary>Database name of the one-MF-context-per-Job-On unique index.</summary>
    public const string JobOnUniqueConstraintName = "mf_contexts_jobon_key";

    /// <summary>Database name of the Job On foreign key.</summary>
    public const string JobOnForeignKeyConstraintName = "FK_mf_contexts_job_ons_jobon_id";

    /// <summary>Database name of the canonical Tool foreign key.</summary>
    public const string ToolForeignKeyConstraintName = "FK_mf_contexts_tools_tool_id";

    /// <summary>Database name of the FK-supporting canonical Tool index.</summary>
    public const string ToolIndexName = "IX_mf_contexts_tool_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MfContextEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("mf_contexts", table =>
        {
            table.HasCheckConstraint("mf_contexts_tool_type_check", "tool_type = 'MF'");
            table.HasCheckConstraint("mf_contexts_tool_reference_required_check", "btrim(tool_reference) <> ''");
            table.HasCheckConstraint("mf_contexts_tool_lot_required_check", "btrim(tool_lot) <> ''");
        });

        builder.HasKey(context => context.MfId);
        builder.Property(context => context.MfId)
            .HasColumnName("mf_id")
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
