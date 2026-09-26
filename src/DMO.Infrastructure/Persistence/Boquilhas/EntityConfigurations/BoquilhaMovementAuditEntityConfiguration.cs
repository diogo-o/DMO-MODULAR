using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>boquilha_movement_audit</c> table: the per-edit audit history.
/// </summary>
/// <remarks>
/// Authority: P2-T07 contract §6.4 (exact columns/CHECK/FKs) — one row per edit in the same
/// transaction as the movement UPDATE; append-only by construction (no UPDATE/DELETE route,
/// repository member or SQL exists).</remarks>
public sealed class BoquilhaMovementAuditEntityConfiguration : IEntityTypeConfiguration<BoquilhaMovementAuditEntity>
{
    /// <summary>Database name of the before/after quantities CHECK.</summary>
    public const string QuantitiesCheckConstraintName = "boquilha_movement_audit_quantities_check";

    /// <summary>Database name of the movement FK.</summary>
    public const string MovementForeignKeyConstraintName = "FK_boquilha_movement_audit_boquilha_movements_movement_id";

    /// <summary>Database name of the editor FK.</summary>
    public const string EditedByUserForeignKeyConstraintName = "FK_boquilha_movement_audit_users_edited_by_user_id";

    /// <summary>Database name of the per-movement trail index.</summary>
    public const string MovementTrailIndexName = "IX_boquilha_movement_audit_movement_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BoquilhaMovementAuditEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("boquilha_movement_audit", table =>
        {
            table.HasCheckConstraint(
                QuantitiesCheckConstraintName,
                "before_quantity > 0 AND after_quantity > 0");
        });

        builder.HasKey(entry => entry.MovementAuditId);
        builder.Property(entry => entry.MovementAuditId)
            .HasColumnName("movement_audit_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(entry => entry.MovementId)
            .HasColumnName("movement_id")
            .IsRequired();

        builder.Property(entry => entry.EditedByUserId)
            .HasColumnName("edited_by_user_id")
            .IsRequired();

        builder.Property(entry => entry.EditedAt)
            .HasColumnName("edited_at")
            .IsRequired();

        builder.Property(entry => entry.BeforeQuantity)
            .HasColumnName("before_quantity")
            .IsRequired();

        builder.Property(entry => entry.AfterQuantity)
            .HasColumnName("after_quantity")
            .IsRequired();

        builder.Property(entry => entry.BeforeBusinessDate)
            .HasColumnName("before_business_date")
            .IsRequired();

        builder.Property(entry => entry.AfterBusinessDate)
            .HasColumnName("after_business_date")
            .IsRequired();

        builder.Property(entry => entry.BeforeMachine)
            .HasColumnName("before_machine");

        builder.Property(entry => entry.AfterMachine)
            .HasColumnName("after_machine");

        builder.Property(entry => entry.BeforeRepairerId)
            .HasColumnName("before_repairer_id");

        builder.Property(entry => entry.AfterRepairerId)
            .HasColumnName("after_repairer_id");

        builder.Property(entry => entry.BeforeObservations)
            .HasColumnName("before_observations");

        builder.Property(entry => entry.AfterObservations)
            .HasColumnName("after_observations");

        builder.Property(entry => entry.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<BoquilhaMovementEntity>()
            .WithMany()
            .HasForeignKey(entry => entry.MovementId)
            .HasConstraintName(MovementForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(entry => entry.EditedByUserId)
            .HasConstraintName(EditedByUserForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        // FK-supporting + the per-movement trail read (edited_at ASC, §19/route 6).
        builder.HasIndex(entry => new { entry.MovementId, entry.EditedAt })
            .HasDatabaseName(MovementTrailIndexName);
    }
}