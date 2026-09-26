using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>boquilha_movements</c> table: the movement/quantity event ledger.
/// </summary>
/// <remarks>
/// Authority: P2-T07 OWNER CLARIFICATION + the preserved shapes. The type CHECK fixes the closed
/// set <c>saida|entrada|entrada_sem_reparacao</c>; the Saída-required CHECK enforces machine +
/// repairer on external dispatch; there is no expected/excess fact pair (superseded). FKs are all
/// RESTRICT; the ledger-order index serves the physical receipt order.
/// </remarks>
public sealed class BoquilhaMovementEntityConfiguration : IEntityTypeConfiguration<BoquilhaMovementEntity>
{
    /// <summary>Database name of the movement-type CHECK (the closed three-type set).</summary>
    public const string MovementTypeCheckConstraintName = "boquilha_movements_type_check";

    /// <summary>Database name of the positive-quantity CHECK.</summary>
    public const string QuantityCheckConstraintName = "boquilha_movements_quantity_check";

    /// <summary>Database name of the machine-code CHECK.</summary>
    public const string MachineCheckConstraintName = "boquilha_movements_machine_check";

    /// <summary>Database name of the Saída-required CHECK (machine + repairer on external dispatch).</summary>
    public const string SaidaRequiredCheckConstraintName = "boquilha_movements_saida_required_check";

    /// <summary>Database name of the observations CHECK.</summary>
    public const string ObservationsCheckConstraintName = "boquilha_movements_observations_check";

    /// <summary>Database name of the movement-version CHECK.</summary>
    public const string VersionCheckConstraintName = "boquilha_movements_version_check";

    /// <summary>Database name of the owning-register FK.</summary>
    public const string BoquilhasForeignKeyConstraintName = "FK_boquilha_movements_boquilhas_boquilhas_id";

    /// <summary>Database name of the repairer FK.</summary>
    public const string RepairerForeignKeyConstraintName = "FK_boquilha_movements_repairers_repairer_id";

    /// <summary>Database name of the recording-actor FK.</summary>
    public const string RecordedByUserForeignKeyConstraintName = "FK_boquilha_movements_users_recorded_by_user_id";

    /// <summary>Database name of the ledger-order index (FK-supporting + physical receipt order).</summary>
    public const string BoquilhasLedgerIndexName = "IX_boquilha_movements_boquilhas_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BoquilhaMovementEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("boquilha_movements", table =>
        {
            table.HasCheckConstraint(
                MovementTypeCheckConstraintName,
                "movement_type IN ('saida','entrada','entrada_sem_reparacao')");
            table.HasCheckConstraint(
                QuantityCheckConstraintName,
                "quantity > 0");
            table.HasCheckConstraint(
                MachineCheckConstraintName,
                "machine IS NULL OR machine IN ('B1','B2','B3','C1','C2','C3')");
            table.HasCheckConstraint(
                SaidaRequiredCheckConstraintName,
                "NOT (movement_type = 'saida' AND (machine IS NULL OR repairer_id IS NULL))");
            table.HasCheckConstraint(
                ObservationsCheckConstraintName,
                "observations IS NULL OR btrim(observations) <> ''");
            table.HasCheckConstraint(
                VersionCheckConstraintName,
                "version >= 1");
        });

        builder.HasKey(movement => movement.MovementId);
        builder.Property(movement => movement.MovementId)
            .HasColumnName("movement_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(movement => movement.BoquilhasId)
            .HasColumnName("boquilhas_id")
            .IsRequired();

        builder.Property(movement => movement.MovementType)
            .HasColumnName("movement_type")
            .IsRequired();

        builder.Property(movement => movement.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        builder.Property(movement => movement.BusinessDate)
            .HasColumnName("business_date")
            .IsRequired();

        builder.Property(movement => movement.RecordedAt)
            .HasColumnName("recorded_at")
            .IsRequired();

        builder.Property(movement => movement.RecordedByUserId)
            .HasColumnName("recorded_by_user_id")
            .IsRequired();

        builder.Property(movement => movement.Machine)
            .HasColumnName("machine");

        builder.Property(movement => movement.RepairerId)
            .HasColumnName("repairer_id");

        builder.Property(movement => movement.Observations)
            .HasColumnName("observations");

        builder.Property(movement => movement.Version)
            .HasColumnName("version")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(movement => movement.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(movement => movement.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<BoquilhaEntity>()
            .WithMany()
            .HasForeignKey(movement => movement.BoquilhasId)
            .HasConstraintName(BoquilhasForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RepairerEntity>()
            .WithMany()
            .HasForeignKey(movement => movement.RepairerId)
            .HasConstraintName(RepairerForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(movement => movement.RecordedByUserId)
            .HasConstraintName(RecordedByUserForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        // FK-supporting + the deterministic physical receipt order (recorded_at ASC,
        // movement_id ASC) of the derived outstanding replay and the ledger display.
        builder.HasIndex(movement => new { movement.BoquilhasId, movement.RecordedAt, movement.MovementId })
            .HasDatabaseName(BoquilhasLedgerIndexName);
    }
}