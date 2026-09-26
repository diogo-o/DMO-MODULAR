using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>boquilhas</c> table: the register identity row of one REAL
/// production/BQ context, with the transitional pré-JobOn <c>tool_id</c> anchor of P2-T07 §34.
/// </summary>
/// <remarks>
/// Authority: P2-T07 OWNER CLARIFICATION §34. EXACTLY ONE anchor per row — the <c>bq_id</c>
/// (production/BQ context, UNIQUE — one register per BQ context) XOR the provisional <c>tool_id</c>
/// (canonical BQ Tool; the association proof by the same UUID) — enforced by the
/// <c>boquilhas_anchor_check</c> CHECK. No lifecycle machinery (no <c>status</c>, no partial
/// unique active-anchor indexes). The register row is updated exactly once, by the association
/// write, guarded by the <c>version</c> token. The FK chain
/// <c>boquilhas.bq_id → bq_contexts → job_ons/tools</c> carries the real production association once
/// settled.</remarks>
public sealed class BoquilhaEntityConfiguration : IEntityTypeConfiguration<BoquilhaEntity>
{
    /// <summary>Database name of the one-register-per-BQ-context unique key.</summary>
    public const string BqIdUniqueConstraintName = "boquilhas_bq_id_key";

    /// <summary>Database name of the production-anchor FK.</summary>
    public const string BqContextForeignKeyConstraintName = "FK_boquilhas_bq_contexts_bq_id";

    /// <summary>Database name of the provisional Tool-anchor FK.</summary>
    public const string ToolForeignKeyConstraintName = "FK_boquilhas_tools_tool_id";

    /// <summary>Database name of the opening-actor FK.</summary>
    public const string CreatedByUserForeignKeyConstraintName = "FK_boquilhas_users_created_by_user_id";

    /// <summary>Database name of the exactly-one-anchor CHECK.</summary>
    public const string AnchorCheckConstraintName = "boquilhas_anchor_check";

    /// <summary>Database name of the association version-token CHECK.</summary>
    public const string VersionCheckConstraintName = "boquilhas_version_check";

    /// <summary>Database name of the FK-supporting index on the register list traversal.</summary>
    public const string CreatedAtIndexName = "IX_boquilhas_created_at";

    /// <summary>Database name of the pending-register-by-Tool read index (§34.1 light packet).</summary>
    public const string ToolIdIndexName = "IX_boquilhas_tool_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BoquilhaEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("boquilhas");

        builder.HasKey(register => register.BoquilhasId);
        builder.Property(register => register.BoquilhasId)
            .HasColumnName("boquilhas_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(register => register.BqId)
            .HasColumnName("bq_id");

        builder.Property(register => register.ToolId)
            .HasColumnName("tool_id");

        builder.Property(register => register.Version)
            .HasColumnName("version")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(register => register.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(register => register.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<BqContextEntity>()
            .WithMany()
            .HasForeignKey(register => register.BqId)
            .HasConstraintName(BqContextForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ToolEntity>()
            .WithMany()
            .HasForeignKey(register => register.ToolId)
            .HasConstraintName(ToolForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(register => register.CreatedByUserId)
            .HasConstraintName(CreatedByUserForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        // One register per REAL production BQ context (the plain unique key; the lifecycle
        // ACTIVE partial unique indexes are superseded and removed).
        builder.HasIndex(register => register.BqId)
            .IsUnique()
            .HasDatabaseName(BqIdUniqueConstraintName);

        // The register list technical order (deterministic; creation order).
        builder.HasIndex(register => register.CreatedAt)
            .HasDatabaseName(CreatedAtIndexName);

        // The §34.1 pending-registers light packet (bq_contexts.tool_id == boquilhas.tool_id).
        builder.HasIndex(register => register.ToolId)
            .HasDatabaseName(ToolIdIndexName);

        // Exactly one anchor — the CHECK backstop of the validator's XOR rule.
        builder.ToTable(t => t.HasCheckConstraint(
            AnchorCheckConstraintName,
            "(bq_id IS NULL) <> (tool_id IS NULL)"));

        // The association version token is always positive (the accepted version discipline).
        builder.ToTable(t => t.HasCheckConstraint(
            VersionCheckConstraintName,
            "version >= 1"));
    }
}